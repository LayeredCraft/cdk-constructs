#!/usr/bin/env bash
#
# Regression tests for build-artifacts.sh path handling and cleanup (F1/F2).
#
# Verifies that:
#   - a relative --out is resolved against the caller's directory and the zip lands there;
#   - no build directory leaks into the source tree;
#   - --clean removes only generated content and never deletes unrelated files in --out;
#   - argument errors exit non-zero.
#
# The .NET workload is used because it exercises the `cd`-based dotnet publish path, which is
# where a relative --out used to break. No Docker or AWS access is required.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD="${SCRIPT_DIR}/build-artifacts.sh"
APP="aspnetcore"

pass() { printf '\033[1;32mPASS\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31mFAIL\033[0m %s\n' "$*" >&2; exit 1; }

WORK="$(mktemp -d)"
cleanup() { rm -rf "${WORK}"; }
trap cleanup EXIT

# 1. Relative --out, run from an unrelated CWD, with a sentinel that must survive --clean.
mkdir -p "${WORK}/rel/out"
echo keep > "${WORK}/rel/out/KEEP.txt"
( cd "${WORK}/rel" && "${BUILD}" --clean --only "${APP}" --out out ) >/dev/null
[[ -f "${WORK}/rel/out/${APP}.zip" ]] || fail "relative --out: zip was not written to the intended directory"
[[ -f "${WORK}/rel/out/KEEP.txt" ]] || fail "relative --out: --clean deleted the sentinel file"
[[ ! -e "${SCRIPT_DIR}/out" ]] || fail "relative --out: a build directory leaked into the source tree"
pass "relative --out resolves against the caller directory and preserves the sentinel"

# 2. Absolute --out with a sentinel.
mkdir -p "${WORK}/abs"
echo keep > "${WORK}/abs/KEEP.txt"
"${BUILD}" --clean --only "${APP}" --out "${WORK}/abs" >/dev/null
[[ -f "${WORK}/abs/${APP}.zip" ]] || fail "absolute --out: zip was not written"
[[ -f "${WORK}/abs/KEEP.txt" ]] || fail "absolute --out: --clean deleted the sentinel file"
pass "absolute --out writes the zip and preserves the sentinel"

# 3. An existing output directory with unrelated content keeps that content across a clean.
mkdir -p "${WORK}/existing/subdir"
echo keep > "${WORK}/existing/unrelated.txt"
"${BUILD}" --clean --only "${APP}" --out "${WORK}/existing" >/dev/null
[[ -f "${WORK}/existing/unrelated.txt" ]] || fail "existing --out: unrelated file was deleted"
[[ -d "${WORK}/existing/subdir" ]] || fail "existing --out: unrelated directory was deleted"
[[ -f "${WORK}/existing/${APP}.zip" ]] || fail "existing --out: zip was not written"
pass "--out pointing at an existing directory never deletes unrelated content"

# 4. Argument errors propagate as non-zero exits.
if "${BUILD}" --only >/dev/null 2>&1; then fail "--only without a value should fail"; fi
if "${BUILD}" --out >/dev/null 2>&1; then fail "--out without a value should fail"; fi
if "${BUILD}" --bogus >/dev/null 2>&1; then fail "an unknown argument should fail"; fi
if "${BUILD}" --only nosuchworkload >/dev/null 2>&1; then fail "an invalid --only value should fail"; fi
pass "argument errors exit non-zero"

printf '\033[1;32mAll tooling regression tests passed\033[0m\n'
