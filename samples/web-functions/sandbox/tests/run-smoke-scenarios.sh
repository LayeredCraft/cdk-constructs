#!/usr/bin/env bash
#
# Offline regression tests for the sandbox smoke-test tooling (T7b second-round fixes).
# Uses stubbed aws/cdk/curl on PATH; performs NO real AWS calls.
set -uo pipefail

TESTS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STUB_BIN="${TESTS_DIR}/stubs"
SANDBOX_DIR="$(cd "${TESTS_DIR}/.." && pwd)"
ARTIFACT_BUILD_DIR="$(cd "${SANDBOX_DIR}/.." && pwd)/build"
# Keep every byte of sandbox state (artifact manifest, smoke summary, revision evidence) inside a private
# temp directory so this suite is isolated from a real deployment and from the lifecycle suite, and does
# not depend on a pre-existing manifest.
export SANDBOX_STATE_DIR="$(mktemp -d)"
trap 'rm -rf "${SANDBOX_STATE_DIR}"' EXIT
MANIFEST="${SANDBOX_STATE_DIR}/artifacts.json"
SUMMARY="${SANDBOX_STATE_DIR}/smoke-summary.json"

export SANDBOX_ALLOW_AWS=1
export SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1
export SANDBOX_ID=wfsbx
export SANDBOX_CONFIRM_DESTROY=wfsbx
export SANDBOX_ACCOUNT=123456789012
export SANDBOX_REGION=us-east-1
export AWS_REGION=us-east-1
export SANDBOX_CDK="${STUB_BIN}/cdk"
export PATH="${STUB_BIN}:${PATH}"
export STUB_STATE=""
export SMOKE_ATTEMPTS=1

# The build id baked into the local v1 artifact; run-workflow.sh derives its expected build id from
# this same file, so the stub must serve it (instead of a fixed placeholder) for the workflow test.
ARTIFACT_BUILD_ID="$(unzip -p "${ARTIFACT_BUILD_DIR}/nextjs.zip" build-id.txt 2>/dev/null | tr -d '\r\n')"
[ -n "${ARTIFACT_BUILD_ID}" ] || ARTIFACT_BUILD_ID="stub-build-v1"
export ARTIFACT_BUILD_ID

PASS=0
FAIL=0
FAILURES=()

ok() { PASS=$((PASS + 1)); }
bad() { FAIL=$((FAIL + 1)); FAILURES+=("$1"); printf 'FAIL: %s\n' "$1" >&2; }
check() { if eval "$2"; then ok; else bad "$1"; fi; }
scenario() { printf '\n=== %s ===\n' "$1" >&2; }
run() { OUT="$("$@" 2>&1)"; RC=$?; }

fresh_state() {
    STUB_STATE="$(mktemp -d)"
    printf '%s\n' "${SANDBOX_ACCOUNT}" >"${STUB_STATE}/account"
    printf '%s\n' "${ARTIFACT_BUILD_ID}" >"${STUB_STATE}/build-id"
    export STUB_STATE
    rm -f "${MANIFEST}" "${SUMMARY}" "${SANDBOX_STATE_DIR}/revision-state.json"
}

deploy_app() { run "${SANDBOX_DIR}/deploy.sh" --revision 1 "$@"; }
run_smoke() { run "${SANDBOX_DIR}/smoke.sh" "$@"; }

summary_field() {
    python3 - "$SUMMARY" "$1" <<'PY'
import json, sys
try:
    data = json.load(open(sys.argv[1]))
except Exception:
    sys.exit(2)
print(json.dumps(data.get(sys.argv[2])))
PY
}

summary_result() { # <status> <name-regex>
    python3 - "$SUMMARY" "$1" "$2" <<'PY'
import json, re, sys
try:
    data = json.load(open(sys.argv[1]))
except Exception:
    sys.exit(2)
pat = re.compile(sys.argv[3])
for r in data.get("results", []):
    if r.get("status") == sys.argv[2] and pat.search(r.get("name", "")):
        sys.exit(0)
sys.exit(1)
PY
}

summary_revision() { # <workload>
    python3 - "$SUMMARY" "$1" <<'PY'
import json, sys
try:
    data = json.load(open(sys.argv[1]))
except Exception:
    sys.exit(2)
print(data.get("revisions", {}).get(sys.argv[2], ""))
PY
}

# --- 1: happy path -------------------------------------------------------------------------------
scenario "1: happy path"
fresh_state
deploy_app
run_smoke
check "1.1 smoke exits 0" '[ "$RC" -eq 0 ]'
check "1.2 summary written" '[ -f "$SUMMARY" ]'
check "1.3 no failures" '[ "$(summary_field fail)" = "0" ]'
check "1.4 Next.js build-id PASS recorded" 'summary_result PASS "Next.js build id"'
check "1.5 Next.js build id is the artifact value" 'grep -q "$ARTIFACT_BUILD_ID" "$SUMMARY"'

# --- 2: S2 signed requests -----------------------------------------------------------------------
scenario "2: signed requests carry curl auth"
check "2.1 curl log written" '[ -f "$STUB_STATE/curl.log" ]'
check "2.2 >=3 signed aspnetcore requests" '[ "$(grep -c "8447 signed=1" "$STUB_STATE/curl.log" 2>/dev/null || echo 0)" -ge 3 ]'

# --- 3: S3 CloudFront ARN (empty region) ---------------------------------------------------------
scenario "3: CloudFront resource-policy checks pass"
check "3.1 Next.js resource policy PASS" 'summary_result PASS "Next.js.*resource policy"'
check "3.2 Blazor resource policy PASS" 'summary_result PASS "Blazor.*resource policy"'

# --- 4-8: S4 fail-closed live policy verification ------------------------------------------------
scenario "4: malformed live policy"
fresh_state
export STUB_POLICY_MODE=malformed
deploy_app
run_smoke
unset STUB_POLICY_MODE
check "4.1 nonzero exit" '[ "$RC" -ne 0 ]'
check "4.2 summary written" '[ -f "$SUMMARY" ]'
check "4.3 policy FAIL recorded" 'summary_result FAIL "resource policy"'

scenario "5: policy parser exception"
fresh_state
export STUB_POLICY_MODE=exception
deploy_app
run_smoke
unset STUB_POLICY_MODE
check "5.1 nonzero exit" '[ "$RC" -ne 0 ]'
check "5.2 policy FAIL recorded" 'summary_result FAIL "resource policy"'

scenario "6: missing live policy"
fresh_state
export STUB_POLICY_MODE=missing
deploy_app
run_smoke
unset STUB_POLICY_MODE
check "6.1 nonzero exit" '[ "$RC" -ne 0 ]'
check "6.2 policy BLOCKED recorded" 'summary_result BLOCKED "resource policy"'

scenario "7: duplicate + unexpected statements"
fresh_state
export STUB_POLICY_MODE=duplicate
deploy_app --second-distribution
run_smoke --second-distribution
unset STUB_POLICY_MODE
check "7.1 duplicate FAIL recorded" 'summary_result FAIL "Next.js.*resource policy"'
fresh_state
export STUB_POLICY_MODE=extra
deploy_app
run_smoke
unset STUB_POLICY_MODE
check "7.2 unexpected grant FAIL recorded" 'summary_result FAIL "Next.js.*resource policy"'

scenario "8: second-distribution grant removed"
fresh_state
export STUB_POLICY_MODE=second-removed
deploy_app --second-distribution
run_smoke --second-distribution
unset STUB_POLICY_MODE
check "8.1 missing second grant FAIL recorded" 'summary_result FAIL "Next.js.*resource policy"'

# --- 9: S5 IAM lookup failure --------------------------------------------------------------------
scenario "9: IAM AccessDenied during policy inspection"
fresh_state
export STUB_FAIL=iam
deploy_app
run_smoke
unset STUB_FAIL
check "9.1 nonzero exit" '[ "$RC" -ne 0 ]'
check "9.2 identity-policy BLOCKED recorded" 'summary_result BLOCKED "policy"'

# --- 10: S6 build-id mismatch --------------------------------------------------------------------
scenario "10: wrong expected build id"
fresh_state
deploy_app
run_smoke --expect-build-id definitely-not-the-build-id
check "10.1 nonzero exit" '[ "$RC" -ne 0 ]'
check "10.2 build-id FAIL recorded" 'summary_result FAIL "build id"'

# --- 11: S7 per-function revision separation -----------------------------------------------------
scenario "11: per-function revisions are distinct"
fresh_state
deploy_app
run_smoke
check "11.1 three distinct revisions" '[ "$(summary_revision nextjs)" != "$(summary_revision aspnetcore)" ] && [ "$(summary_revision aspnetcore)" != "$(summary_revision blazor)" ] && [ "$(summary_revision nextjs)" != "$(summary_revision blazor)" ]'

# --- 12: S7 v1 -> v2 revision transition ---------------------------------------------------------
scenario "12: v1 -> v2 revision transition"
fresh_state
deploy_app
run_smoke
cp "${SUMMARY}" "${STUB_STATE}/baseline.json"
export STUB_REVISION=2
run_smoke --revision-baseline "${STUB_STATE}/baseline.json"
check "12.1 transition PASS recorded" 'summary_result PASS "revision transition"'
check "12.2 transition run exits 0" '[ "$RC" -eq 0 ]'
cp "${SUMMARY}" "${STUB_STATE}/baseline2.json"
run_smoke --revision-baseline "${STUB_STATE}/baseline2.json"
check "12.3 unchanged revisions fail" '[ "$RC" -ne 0 ]'
unset STUB_REVISION

# --- 13: L1 destroy does not delete log groups ---------------------------------------------------
scenario "13: destroy with no owned stack leaves log groups"
fresh_state
run "${SANDBOX_DIR}/destroy.sh" --yes
check "13.1 destroy exits 0" '[ "$RC" -eq 0 ]'
check "13.2 no delete-log-group call" '! grep -q "logs delete-log-group" "$STUB_STATE/calls.log" 2>/dev/null'
check "13.3 no delete-bucket call" '[ ! -f "$STUB_STATE/delete_bucket_called" ]'

# --- 14: L2 mismatched manifest -> zero mutations ------------------------------------------------
scenario "14: --skip-upload with mismatched manifest"
fresh_state
printf '{"sandboxId":"other","account":"%s","region":"%s","prefix":"%s","artifactsBucket":"x","externalBucket":"y","versions":{}}' \
    "${SANDBOX_ACCOUNT}" "${SANDBOX_REGION}" "${SANDBOX_ID}" >"${MANIFEST}"
run "${SANDBOX_DIR}/deploy.sh" --revision 1 --skip-upload
check "14.1 nonzero exit" '[ "$RC" -ne 0 ]'
check "14.2 no cdk deploy" '! grep -q "^cdk deploy" "$STUB_STATE/calls.log" 2>/dev/null'
check "14.3 no stacks created" '[ ! -d "$STUB_STATE/stacks" ] || [ -z "$(ls -A "$STUB_STATE/stacks" 2>/dev/null)" ]'
rm -f "${MANIFEST}"

# --- 15: L3 fresh workflow without a manifest ----------------------------------------------------
scenario "15: fresh run-workflow"
fresh_state
run "${SANDBOX_DIR}/run-workflow.sh" --revision 1
check "15.1 workflow exits 0" '[ "$RC" -eq 0 ]'
check "15.2 summary written" '[ -f "$SUMMARY" ]'

# --- 16: SM1 multiple failures still produce a complete summary ----------------------------------
scenario "16: multiple failures still summarize"
fresh_state
export STUB_POLICY_MODE=malformed
export STUB_FAIL=iam
deploy_app
run_smoke
unset STUB_POLICY_MODE STUB_FAIL
check "16.1 nonzero exit" '[ "$RC" -ne 0 ]'
check "16.2 failure count > 0" '[ "$(summary_field fail)" -gt 0 ]'
check "16.3 results non-empty" 'python3 -c "import json,sys; sys.exit(0 if json.load(open(\"$SUMMARY\"))[\"results\"] else 1)"'

# --- 17-18: missing/malformed revision API is BLOCKED, never PASS --------------------------------
scenario "17: revision API unavailable is BLOCKED"
fresh_state
export STUB_FAIL=webfunction
deploy_app
run_smoke
unset STUB_FAIL
check "17.1 nonzero exit" '[ "$RC" -ne 0 ]'
check "17.2 revision identity BLOCKED" 'summary_result BLOCKED "revision identity"'
check "17.3 no revision identity PASS" '! summary_result PASS "revision identity"'

scenario "18: malformed revision API response is BLOCKED"
fresh_state
export STUB_FAIL=webfunction-shape
deploy_app
run_smoke
unset STUB_FAIL
check "18.1 nonzero exit" '[ "$RC" -ne 0 ]'
check "18.2 revision identity BLOCKED" 'summary_result BLOCKED "revision identity"'
check "18.3 no revision identity PASS" '! summary_result PASS "revision identity"'

printf '\nSmoke regression scenarios: %d passed, %d failed\n' "${PASS}" "${FAIL}"
if [ "${FAIL}" -gt 0 ]; then
    printf 'Failures:\n' >&2
    for f in "${FAILURES[@]}"; do printf '  - %s\n' "$f" >&2; done
    exit 1
fi
exit 0
