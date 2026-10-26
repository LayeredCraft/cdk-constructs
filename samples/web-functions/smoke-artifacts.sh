#!/usr/bin/env bash
#
# Local smoke tests for the Web Function artifacts produced by build-artifacts.sh.
#
# Every workload is exercised through its real entrypoint: the extracted artifact is mounted
# at /var/task inside the AWS Lambda Node.js 24 base image and `node /var/task/index.js` is
# run. For the .NET workloads that means the Node.js execve shim actually replaces Node with
# the self-contained apphost, so the deployed runtime contract is what gets tested (not a
# direct launch of the executable).
#
# Each artifact family is checked in both its v1 and v2 form (they differ only by the baked
# build id), and a mock Lambda runtime API verifies the readiness handshake the .NET servers
# must perform.
#
# No AWS access or credentials are required. Docker is required: if it is unavailable the
# container-backed checks are reported as SKIPPED and runtime validation is NOT claimed.
#
# Usage:
#   ./smoke-artifacts.sh                       # smoke every built artifact (v1 and v2)
#   ./smoke-artifacts.sh --only blazor         # one artifact (accepts nextjs-v2, etc.)
#   ./smoke-artifacts.sh --artifacts /tmp/out

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ARTIFACT_DIR="${SCRIPT_DIR}/build"
MOCK_SCRIPT="${SCRIPT_DIR}/tools/mock-runtime-api.mjs"

# The real Lambda runtime image: Node 24 on the same AL2023 base the managed runtime uses,
# so a passing run also demonstrates base-image compatibility.
LAMBDA_NODE_IMAGE="public.ecr.aws/lambda/nodejs:24"
PLATFORM="linux/arm64"

ARTIFACTS=(nextjs nextjs-v2 aspnetcore aspnetcore-v2 blazor blazor-v2)

ONLY=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --artifacts) ARTIFACT_DIR="$2"; shift 2 ;;
    --only) ONLY="$2"; shift 2 ;;
    -h|--help)
      sed -n '2,21p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [[ -n "${ONLY}" ]]; then
  match=0
  for a in "${ARTIFACTS[@]}"; do [[ "${ONLY}" == "${a}" ]] && match=1; done
  [[ ${match} -eq 1 ]] || { echo "error: --only must be one of: ${ARTIFACTS[*]}" >&2; exit 2; }
fi

log()  { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
skip() { printf '\033[1;33mSKIP\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31merror:\033[0m %s\n' "$*" >&2; exit 1; }

CLEANUP_PIDS=()
CLEANUP_CONTAINERS=()
CLEANUP_PATHS=()

cleanup() {
  local c p d
  # Containers are started without --rm so their logs survive a failure; remove them here.
  for c in ${CLEANUP_CONTAINERS[@]+"${CLEANUP_CONTAINERS[@]}"}; do
    docker rm -f "${c}" >/dev/null 2>&1 || true
  done
  for p in ${CLEANUP_PIDS[@]+"${CLEANUP_PIDS[@]}"}; do
    kill "${p}" 2>/dev/null || true
  done
  for d in ${CLEANUP_PATHS[@]+"${CLEANUP_PATHS[@]}"}; do
    [[ -n "${d}" ]] && rm -rf "${d}"
  done
}
trap cleanup EXIT

should_smoke() { [[ -z "${ONLY}" || "${ONLY}" == "$1" ]]; }

# Work directories live under the artifact directory rather than the OS temp directory so
# Docker Desktop can bind-mount them (its default file sharing does not include the
# macOS TMPDIR under /var/folders).
WORK_ROOT="${ARTIFACT_DIR}/.smoke"
make_work_dir() {
  mkdir -p "${WORK_ROOT}"
  mktemp -d "${WORK_ROOT}/$1.XXXXXX"
}

free_port() {
  node -e 'const s=require("net").createServer();s.listen(0,"127.0.0.1",()=>{console.log(s.address().port);s.close();});'
}

wait_for_http() {
  local url="$1" timeout="${2:-30}"
  local deadline=$(( SECONDS + timeout ))
  while (( SECONDS < deadline )); do
    if curl -fsS -o /dev/null "${url}" 2>/dev/null; then
      return 0
    fi
    sleep 0.5
  done
  return 1
}

assert_contains() {
  local haystack="$1" needle="$2" label="$3"
  if [[ "${haystack}" != *"${needle}"* ]]; then
    die "${label}: expected to find '${needle}' in the response"
  fi
  log "  ok: ${label}"
}

docker_available() {
  command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1
}

show_diagnostics() {
  local artifact="$1"
  printf '\033[1;31m--- %s: container logs ---\033[0m\n' "${artifact}" >&2
  docker logs "${CONTAINER}" >&2 2>&1 || true
  if [[ -f "${WORK_DIR}/mock.log" ]]; then
    printf '\033[1;31m--- %s: mock runtime API logs ---\033[0m\n' "${artifact}" >&2
    cat "${WORK_DIR}/mock.log" >&2 || true
  fi
  printf '\033[1;31m--- end diagnostics ---\033[0m\n' >&2
}

# --- Static artifact checks (no Docker required) -----------------------------------------

required_entries() {
  case "$1" in
    nextjs)     printf '%s\n' index.js server.js package.json public/sample.txt build-id.txt ;;
    aspnetcore) printf '%s\n' index.js server server.dll build-id.txt ;;
    blazor)     printf '%s\n' index.js server server.dll wwwroot/app.css build-id.txt ;;
  esac
}

static_check() {
  local artifact="$1" base="${1%-v2}"
  local zip="${ARTIFACT_DIR}/${artifact}.zip"
  [[ -f "${zip}" ]] || die "missing artifact: ${zip}"

  local entries
  entries="$(unzip -Z1 "${zip}")"

  local required
  while IFS= read -r required; do
    grep -qxF "${required}" <<<"${entries}" || die "${artifact}: archive is missing '${required}'"
  done < <(required_entries "${base}")

  if [[ "${base}" != "nextjs" ]]; then
    local probe
    probe="$(mktemp -d)"
    unzip -oq "${zip}" server -d "${probe}"
    [[ -x "${probe}/server" ]] || die "${artifact}: 'server' is not executable after extraction"
    rm -rf "${probe}"
  fi

  # The Next.js bundle must carry linux-arm64 native modules, never the build host's.
  if [[ "${base}" == "nextjs" ]]; then
    if grep -qiE '(@img/(sharp|sharp-libvips)-[a-z0-9]+-(darwin|win32))' <<<"${entries}"; then
      die "${artifact}: archive contains non-linux native modules"
    fi
    grep -qE '@img/sharp-linux-arm64/' <<<"${entries}" || die "${artifact}: missing @img/sharp-linux-arm64"
    grep -qE '@img/sharp-libvips-linux-arm64/' <<<"${entries}" || die "${artifact}: missing @img/sharp-libvips-linux-arm64"
  fi

  local id
  id="$(unzip -p "${zip}" build-id.txt | tr -d '\r\n')"
  [[ -n "${id}" ]] || die "${artifact}: build-id.txt is empty"
  log "  ok: ${artifact}: static checks pass (build id '${id}')"
}

# --- Container-backed runtime checks -----------------------------------------------------

# Starts the artifact in the Lambda Node.js base image via `node index.js` and a mock
# runtime API. Sets BASE_URL, MOCK_URL, CONTAINER, WORK_DIR and EXPECTED_BUILD_ID.
start_bundle() {
  local artifact="$1"
  local zip="${ARTIFACT_DIR}/${artifact}.zip"
  [[ -f "${zip}" ]] || die "missing artifact: ${zip}"

  local tmp port mock_port container expected
  tmp="$(make_work_dir "${artifact}")"; CLEANUP_PATHS+=("${tmp}")
  unzip -oq "${zip}" -d "${tmp}"
  chmod +x "${tmp}/server" 2>/dev/null || true

  [[ -f "${tmp}/index.js" ]] || die "${artifact}: index.js missing from artifact"
  [[ -f "${tmp}/build-id.txt" ]] || die "${artifact}: build-id.txt missing from artifact"
  expected="$(tr -d '\r\n' < "${tmp}/build-id.txt")"
  [[ -n "${expected}" ]] || die "${artifact}: build-id.txt is empty"

  port="$(free_port)"
  mock_port="$(free_port)"
  container="wf-smoke-${artifact}-$$"; CLEANUP_CONTAINERS+=("${container}")

  node "${MOCK_SCRIPT}" "${mock_port}" >"${tmp}/mock.log" 2>&1 &
  local mock_pid=$!; CLEANUP_PIDS+=("${mock_pid}"); disown "${mock_pid}" 2>/dev/null || true
  sleep 0.5

  log "${artifact}: running index.js in ${LAMBDA_NODE_IMAGE} (${PLATFORM})"
  docker run -d \
    --name "${container}" \
    --platform "${PLATFORM}" \
    --entrypoint node \
    -p "127.0.0.1:${port}:${port}" \
    --add-host "host.docker.internal:host-gateway" \
    -e "AWS_LAMBDA_HTTP_ENDPOINT=0.0.0.0:${port}" \
    -e "AWS_LAMBDA_RUNTIME_API=host.docker.internal:${mock_port}" \
    -v "${tmp}:/var/task:ro" \
    -w /var/task \
    "${LAMBDA_NODE_IMAGE}" /var/task/index.js >/dev/null

  BASE_URL="http://127.0.0.1:${port}"
  MOCK_URL="http://127.0.0.1:${mock_port}"
  CONTAINER="${container}"
  WORK_DIR="${tmp}"
  EXPECTED_BUILD_ID="${expected}"
}

await_http() {
  local artifact="$1" url="$2" timeout="${3:-60}"
  if ! wait_for_http "${url}" "${timeout}"; then
    show_diagnostics "${artifact}"
    die "${artifact}: server did not become ready at ${url}"
  fi
}

assert_readiness() {
  local artifact="$1"
  local deadline=$(( SECONDS + 20 ))
  while (( SECONDS < deadline )); do
    if curl -fsS "${MOCK_URL}/__ready" >/dev/null 2>&1; then
      log "  ok: ${artifact}: readiness POST reached the mock runtime API"
      return 0
    fi
    sleep 0.5
  done
  show_diagnostics "${artifact}"
  die "${artifact}: readiness POST was not received by the mock runtime API"
}

smoke_nextjs() {
  local artifact="$1"
  start_bundle "${artifact}"
  await_http "${artifact}" "${BASE_URL}/" 60

  local page
  page="$(curl -fsS "${BASE_URL}/")"
  assert_contains "${page}" "Next.js Web Function Sample" "${artifact}: SSR heading"
  assert_contains "${page}" "id=\"build-id\"" "${artifact}: build id element rendered"
  assert_contains "${page}" "${EXPECTED_BUILD_ID}" "${artifact}: page reflects build-id.txt"

  local asset
  asset="$(curl -fsS "${BASE_URL}/sample.txt")"
  assert_contains "${asset}" "static asset" "${artifact}: public asset served"

  local chunk
  chunk="$(printf '%s' "${page}" | grep -oE '/_next/static/[^" ]+\.js' | head -1 || true)"
  [[ -n "${chunk}" ]] || die "${artifact}: no /_next/static/*.js reference found in the rendered page"
  local code
  code="$(curl -sS -o /dev/null -w '%{http_code}' "${BASE_URL}${chunk}")"
  [[ "${code}" == "200" ]] || die "${artifact}: static chunk ${chunk} returned HTTP ${code}"
  log "  ok: ${artifact}: static chunk served (${chunk})"

  log "${artifact}: PASS"
}

smoke_aspnetcore() {
  local artifact="$1"
  start_bundle "${artifact}"
  await_http "${artifact}" "${BASE_URL}/health" 60

  local health status
  health="$(curl -fsS "${BASE_URL}/health")"
  assert_contains "${health}" '"backend":"aspnetcore"' "${artifact}: /health backend"
  assert_contains "${health}" "\"buildId\":\"${EXPECTED_BUILD_ID}\"" "${artifact}: /health reflects build-id.txt"

  status="$(curl -fsS "${BASE_URL}/status")"
  assert_contains "${status}" "requestTimeUtc" "${artifact}: /status dynamic field"

  assert_readiness "${artifact}"
  log "${artifact}: PASS"
}

smoke_blazor() {
  local artifact="$1"
  start_bundle "${artifact}"
  await_http "${artifact}" "${BASE_URL}/" 60

  local page css
  page="$(curl -fsS "${BASE_URL}/")"
  assert_contains "${page}" "Blazor Static SSR Web Function Sample" "${artifact}: rendered page"

  css="$(curl -fsS "${BASE_URL}/app.css")"
  assert_contains "${css}" "#512bd4" "${artifact}: app.css served"

  local code
  code="$(curl -sS -o /dev/null -w '%{http_code}' "${BASE_URL}/_framework/blazor.web.js")"
  [[ "${code}" == "200" ]] || die "${artifact}: _framework/blazor.web.js returned HTTP ${code}"
  log "  ok: ${artifact}: framework asset served"

  assert_readiness "${artifact}"
  log "${artifact}: PASS"
}

# --- Drive -------------------------------------------------------------------------------

log "Checking Node.js entry points parse"
for shim in "${SCRIPT_DIR}"/apps/*/index.js; do
  node --check "${shim}"
  log "  ok: $(basename "$(dirname "${shim}")")/index.js parses"
done

log "Static artifact checks"
for artifact in "${ARTIFACTS[@]}"; do
  should_smoke "${artifact}" || continue
  static_check "${artifact}"
done

if ! docker_available; then
  skip "Docker is unavailable: container-backed smoke tests were SKIPPED for all workloads"
  skip "Runtime/HTTP validation was NOT performed; only the static artifact checks above ran"
  log "Smoke tests complete"
  exit 0
fi

for artifact in "${ARTIFACTS[@]}"; do
  should_smoke "${artifact}" || continue
  "smoke_${artifact%-v2}" "${artifact}"
done

# The v2 artifacts must differ from v1 only by the baked build id.
log "Checking v1/v2 build-id differences"
for base in nextjs aspnetcore blazor; do
  should_smoke "${base}" || should_smoke "${base}-v2" || continue
  v1="${ARTIFACT_DIR}/${base}.zip"
  v2="${ARTIFACT_DIR}/${base}-v2.zip"
  [[ -f "${v1}" && -f "${v2}" ]] || continue
  id1="$(unzip -p "${v1}" build-id.txt | tr -d '\r\n')"
  id2="$(unzip -p "${v2}" build-id.txt | tr -d '\r\n')"
  [[ "${id1}" != "${id2}" ]] || die "${base}: v1 and v2 share build id '${id1}'"
  [[ "${id2}" == "${id1}-v2" ]] || die "${base}: v2 build id '${id2}' is not '${id1}-v2'"
  log "  ok: ${base}: v1='${id1}' v2='${id2}'"
done

log "Smoke tests complete"
