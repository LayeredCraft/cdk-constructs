#!/usr/bin/env bash
#
# Build deployment-ready Web Function artifacts for the three sandbox workloads.
#
# Produces, in samples/web-functions/build/ (override with --out):
#   nextjs.zip        aspnetcore.zip        blazor.zip
#   nextjs-v2.zip     aspnetcore-v2.zip     blazor-v2.zip
#
# Each zip is a self-contained Lambda Web Function revision payload: the execve shim (or
# the Next.js entry) at the zip root, the server entrypoint at the zip root, and every
# runtime file the server needs. The -v2 variants carry a different build id so a
# revision-update test can observe a change between two artifact versions.
#
# Builds are repeatable but NOT byte-for-byte reproducible: zip records file modification
# times and stores entries in directory order, and the Next.js build stamps its own
# generated identifiers. Two runs from the same source therefore differ at the byte level.
#
# No AWS access or credentials are required: this only compiles and packages.
#
# Prerequisites:
#   - .NET 10 SDK (GA)                     -> https://dotnet.microsoft.com/download
#   - Node.js 24+ and npm                  -> https://nodejs.org
#   - zip / unzip                          (preinstalled on macOS and most Linux)
#
# Usage:
#   ./build-artifacts.sh                 # build all three workloads (+ v2 variants)
#   ./build-artifacts.sh --clean         # remove generated artifacts first, then build
#   ./build-artifacts.sh --build-id abc  # override the baked build id
#   ./build-artifacts.sh --out /tmp/out  # override the output directory (relative ok)
#   ./build-artifacts.sh --only nextjs   # build a single workload

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APPS_DIR="${SCRIPT_DIR}/apps"

RUNTIME_IDENTIFIER="linux-arm64"
TARGET_FRAMEWORK="net10.0"

# Web Functions run on Graviton (linux/arm64) only. Install the Next.js optional
# dependencies for that platform so the standalone bundle carries linux-arm64 native
# modules (sharp/libvips) instead of whatever the build host happens to be.
NPM_PLATFORM_ARGS=(--os=linux --cpu=arm64 --libc=glibc)

WORKLOADS=(nextjs aspnetcore blazor)

OUT_DIR="${SCRIPT_DIR}/build"
CLEAN=0
ONLY=""

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
die() { printf '\033[1;31merror:\033[0m %s\n' "$*" >&2; exit 1; }
arg_error() { printf '\033[1;31merror:\033[0m %s\n' "$*" >&2; exit 2; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --clean) CLEAN=1; shift ;;
    --build-id) [[ $# -ge 2 ]] || arg_error "--build-id requires a value"; BUILD_ID="$2"; shift 2 ;;
    --out) [[ $# -ge 2 ]] || arg_error "--out requires a value"; OUT_DIR="$2"; shift 2 ;;
    --only) [[ $# -ge 2 ]] || arg_error "--only requires a value"; ONLY="$2"; shift 2 ;;
    -h|--help)
      sed -n '2,30p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) arg_error "unknown argument: $1" ;;
  esac
done

if [[ -n "${ONLY}" ]]; then
  match=0
  for w in "${WORKLOADS[@]}"; do [[ "${ONLY}" == "${w}" ]] && match=1; done
  [[ ${match} -eq 1 ]] || arg_error "--only must be one of: ${WORKLOADS[*]}"
fi

# Resolve the output directory to an absolute path before any `cd`. A relative --out is
# interpreted against the caller's directory, and later `cd`-based steps (the dotnet
# publish working directory, the zip staging directory) must not re-interpret it, which
# would scatter build output into the source tree.
case "${OUT_DIR}" in
  /*) ;;
  *) OUT_DIR="${PWD}/${OUT_DIR}" ;;
esac

STAGE_DIR="${OUT_DIR}/stage"

# A stable build id by default so repeated builds from the same commit share an id.
if [[ -z "${BUILD_ID:-}" ]]; then
  BUILD_ID="$(git -C "${SCRIPT_DIR}" rev-parse --short=12 HEAD 2>/dev/null || echo dev)"
fi

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "required command not found: $1"
}

require_cmd zip
require_cmd unzip

# Remove only content this script generates. The output directory itself is never deleted:
# a caller may point --out at an existing directory that also holds unrelated files, so a
# blanket `rm -rf "${OUT_DIR}"` would destroy them.
clean_outputs() {
  local name
  for name in "${WORKLOADS[@]}"; do
    rm -f "${OUT_DIR}/${name}.zip" "${OUT_DIR}/${name}-v2.zip"
  done
  rm -rf "${OUT_DIR}/stage" "${OUT_DIR}/.smoke"
}

if [[ ${CLEAN} -eq 1 ]]; then
  log "Removing generated artifacts in ${OUT_DIR}"
  clean_outputs
fi

mkdir -p "${STAGE_DIR}"

should_build() {
  [[ -z "${ONLY}" || "${ONLY}" == "$1" ]]
}

# Zip the contents of a staging directory so archive entries are relative to the root
# (the layout Lambda extracts to /var/task). -X keeps the archives free of host-specific
# extra attributes while still storing the Unix permission bits.
zip_stage() {
  local stage="$1" archive="$2"
  rm -f "${archive}"
  ( cd "${stage}" && zip -q -r -X "${archive}" . )
}

# Assert a set of required entries exists in an archive, and that `server` (when present)
# round-trips with its executable bit set.
validate_zip() {
  local archive="$1" name="$2"
  shift 2
  local entries
  entries="$(unzip -Z1 "${archive}")"

  local required
  for required in "$@"; do
    if ! grep -qxF "${required}" <<<"${entries}"; then
      die "${name}: archive is missing required entry '${required}'"
    fi
  done

  if grep -qxF "server" <<<"${entries}"; then
    local probe
    probe="$(mktemp -d)"
    unzip -oq "${archive}" server -d "${probe}"
    if [[ ! -x "${probe}/server" ]]; then
      die "${name}: 'server' entry is not executable after extraction"
    fi
    rm -rf "${probe}"
  fi

  local size
  size="$(du -h "${archive}" | cut -f1 | tr -d ' ')"
  log "Validated ${name} (${size}): ${archive}"
}

write_build_id() {
  printf '%s\n' "$1" > "$2"
}

# --- Next.js -----------------------------------------------------------------------------

build_nextjs() {
  local app_dir="${APPS_DIR}/nextjs"
  local stage="${STAGE_DIR}/nextjs"
  local zip="${OUT_DIR}/nextjs.zip"

  log "Building Next.js standalone output (${NPM_PLATFORM_ARGS[*]})"
  rm -rf "${stage}" "${app_dir}/.next"
  mkdir -p "${stage}"

  (
    cd "${app_dir}"
    if [[ -f package-lock.json ]]; then
      npm ci --no-audit --no-fund "${NPM_PLATFORM_ARGS[@]}"
    else
      npm install --no-audit --no-fund "${NPM_PLATFORM_ARGS[@]}"
    fi
    npm run build
  )

  # The zip root is the standalone server plus the static/public assets the recipe calls
  # for: standalone output + .next/static + public, with index.js alongside server.js.
  cp -R "${app_dir}/.next/standalone/." "${stage}/"
  mkdir -p "${stage}/.next"
  cp -R "${app_dir}/.next/static" "${stage}/.next/static"
  cp -R "${app_dir}/public" "${stage}/public"
  cp "${app_dir}/index.js" "${stage}/index.js"
  write_build_id "${BUILD_ID}" "${stage}/build-id.txt"

  zip_stage "${stage}" "${zip}"
  validate_zip "${zip}" "nextjs" index.js server.js package.json public/sample.txt build-id.txt

  make_v2 "${stage}" "${zip}"
}

# --- ASP.NET Core ------------------------------------------------------------------------

build_aspnetcore() {
  local app_dir="${APPS_DIR}/aspnetcore"
  local stage="${STAGE_DIR}/aspnetcore"
  local zip="${OUT_DIR}/aspnetcore.zip"

  log "Publishing ASP.NET Core self-contained ${RUNTIME_IDENTIFIER}"
  rm -rf "${stage}"
  mkdir -p "${stage}"

  (
    cd "${SCRIPT_DIR}"
    dotnet publish "${app_dir}/Acme.AspNetCore.csproj" \
      --configuration Release \
      --runtime "${RUNTIME_IDENTIFIER}" \
      --self-contained true \
      -p:PublishTrimmed=false \
      -p:TargetFramework="${TARGET_FRAMEWORK}" \
      --output "${stage}"
  )

  cp "${app_dir}/index.js" "${stage}/index.js"
  write_build_id "${BUILD_ID}" "${stage}/build-id.txt"

  zip_stage "${stage}" "${zip}"
  validate_zip "${zip}" "aspnetcore" index.js server server.dll build-id.txt

  make_v2 "${stage}" "${zip}"
}

# --- Blazor Static SSR -------------------------------------------------------------------

build_blazor() {
  local app_dir="${APPS_DIR}/blazor"
  local stage="${STAGE_DIR}/blazor"
  local zip="${OUT_DIR}/blazor.zip"

  log "Publishing Blazor Static SSR self-contained ${RUNTIME_IDENTIFIER}"
  rm -rf "${stage}"
  mkdir -p "${stage}"

  (
    cd "${SCRIPT_DIR}"
    dotnet publish "${app_dir}/Acme.Blazor.csproj" \
      --configuration Release \
      --runtime "${RUNTIME_IDENTIFIER}" \
      --self-contained true \
      -p:PublishTrimmed=false \
      -p:TargetFramework="${TARGET_FRAMEWORK}" \
      --output "${stage}"
  )

  cp "${app_dir}/index.js" "${stage}/index.js"
  write_build_id "${BUILD_ID}" "${stage}/build-id.txt"

  zip_stage "${stage}" "${zip}"
  validate_zip "${zip}" "blazor" index.js server server.dll wwwroot/app.css build-id.txt

  make_v2 "${stage}" "${zip}"
}

# Produce a second archive that differs only by the baked build id, so a revision-update
# test has two distinct artifact versions to move between.
make_v2() {
  local stage="$1" zip="$2"
  local v2_stage="${stage}-v2"
  local v2_zip="${zip%.zip}-v2.zip"

  rm -rf "${v2_stage}"
  cp -R "${stage}" "${v2_stage}"
  write_build_id "${BUILD_ID}-v2" "${v2_stage}/build-id.txt"

  zip_stage "${v2_stage}" "${v2_zip}"
  log "Built v2 variant: ${v2_zip}"
}

# --- Drive -------------------------------------------------------------------------------

log "Build id: ${BUILD_ID}"
log "Output directory: ${OUT_DIR}"

should_build nextjs && build_nextjs
should_build aspnetcore && build_aspnetcore
should_build blazor && build_blazor

log "Artifacts:"
for archive in "${OUT_DIR}"/*.zip; do
  [[ -e "${archive}" ]] && printf '  %s\n' "${archive}"
done
