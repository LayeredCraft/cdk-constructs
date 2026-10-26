#!/usr/bin/env bash
#
# Offline lifecycle validation for the Web Function sandbox tooling.
#
# Exercises deploy/upload/destroy/validate against stubbed `aws` and `cdk` commands so the full
# deployment lifecycle is verified without touching real AWS. Every scenario asserts command order,
# exit codes, and resulting state.

set -uo pipefail

TESTS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STUB_BIN="${TESTS_DIR}/stubs"
CDK_STUB="${STUB_BIN}/cdk"
SANDBOX_DIR="$(cd "${TESTS_DIR}/.." && pwd)"

export SANDBOX_ALLOW_AWS=1
export SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1
export SANDBOX_ID=wfsbx
export SANDBOX_CONFIRM_DESTROY=wfsbx
export SANDBOX_ACCOUNT=123456789012
export SANDBOX_REGION=us-east-1
export AWS_REGION=us-east-1
export SANDBOX_CDK="${CDK_STUB}"
export PATH="${STUB_BIN}:${PATH}"
export STUB_STATE=""

PASS=0
FAIL=0
declare -a FAILURES=()

ok() { PASS=$((PASS + 1)); printf '    PASS  %s\n' "$1"; }
bad() { FAIL=$((FAIL + 1)); FAILURES+=("$1"); printf '    FAIL  %s\n' "$1"; }
check() { if eval "$2"; then ok "$1"; else bad "$1"; fi; }
scenario() { printf '\n== %s\n' "$1"; }

OUT=""
RC=0
run() { OUT="$("$@" 2>&1)"; RC=$?; }

fresh_state() {
    STUB_STATE="$(mktemp -d)"
    echo "${SANDBOX_ACCOUNT}" >"${STUB_STATE}/account"
    export STUB_STATE
}

A_STACK="lccdk-wf-sandbox-wfsbx-artifacts"
E_STACK="lccdk-wf-sandbox-wfsbx-external"
APP_STACK="lccdk-wf-sandbox-wfsbx-app"
A_BUCKET="wfsbx-artifacts-123456789012-us-east-1"
E_BUCKET="wfsbx-external-123456789012-us-east-1"
MANIFEST="${SANDBOX_DIR}/build/artifacts.json"

cleanup_manifest() { rm -f "${MANIFEST}"; }

# --------------------------------------------------------------------------------------------------
scenario "1: environment validation (read-only)"
fresh_state
cleanup_manifest
run "${SANDBOX_DIR}/validate-env.sh" --check-aws
check "1.1 exit 0" '[ "$RC" -eq 0 ]'
check "1.2 no mutations (no stacks)" '[ -z "$(ls -A "$STUB_STATE/stacks" 2>/dev/null)" ]'

# --------------------------------------------------------------------------------------------------
scenario "2-8: full lifecycle (deploy -> upload -> deploy -> update -> second dist -> destroy)"
fresh_state
cleanup_manifest

run "${SANDBOX_DIR}/deploy.sh" --stacks-only
check "2.1 stacks-only exit 0" '[ "$RC" -eq 0 ]'
check "2.2 artifacts stack created" '[ -f "$STUB_STATE/stacks/'"$A_STACK"'.json" ]'
check "2.3 external stack created" '[ -f "$STUB_STATE/stacks/'"$E_STACK"'.json" ]'
check "2.4 app stack NOT created yet" '[ ! -f "$STUB_STATE/stacks/'"$APP_STACK"'.json" ]'
check "2.5 support deploy used COMPONENTS=all" 'grep -q "COMPONENTS=all" "$STUB_STATE/calls.log"'
check "2.6 never used reduced COMPONENTS" '! grep -q "COMPONENTS=artifacts,external" "$STUB_STATE/calls.log"'
check "2.7 support deploy ran with placeholder versions" 'grep "deploy .*'"$A_STACK"'" "$STUB_STATE/calls.log" | grep -q "PLACEHOLDER=1"'

run "${SANDBOX_DIR}/upload-artifacts.sh" --revision 1
check "3.1 upload v1 exit 0" '[ "$RC" -eq 0 ]'
check "3.2 manifest v1 present" '[ -f "$MANIFEST" ] && python3 -c "import json;d=json.load(open(\"$MANIFEST\"));assert d[\"versions\"][\"v1\"][\"nextjs\"]" 2>/dev/null'
check "3.3 manifest records target" 'python3 -c "import json;d=json.load(open(\"$MANIFEST\"));assert d[\"sandboxId\"]==\"wfsbx\" and d[\"account\"]==\"123456789012\" and d[\"region\"]==\"us-east-1\"" 2>/dev/null'

run "${SANDBOX_DIR}/deploy.sh" --revision 1
check "4.1 deploy v1 exit 0" '[ "$RC" -eq 0 ]'
check "4.2 app stack created" '[ -f "$STUB_STATE/stacks/'"$APP_STACK"'.json" ]'
check "4.3 app deploy used real versions (no placeholder)" '! grep "deploy .*'"$APP_STACK"'" "$STUB_STATE/calls.log" | grep -q "PLACEHOLDER=1"'
check "4.4 app log groups created" '[ -f "$STUB_STATE/logs/aws/lambda/web/wfsbx-nextjs" ] && [ -f "$STUB_STATE/logs/aws/lambda/web/wfsbx-aspnetcore" ] && [ -f "$STUB_STATE/logs/aws/lambda/web/wfsbx-blazor" ]'

run "${SANDBOX_DIR}/deploy.sh" --revision 1 --skip-upload
check "5.1 repeat deploy (skip-upload) exit 0" '[ "$RC" -eq 0 ]'
check "5.2 app stack still present" '[ -f "$STUB_STATE/stacks/'"$APP_STACK"'.json" ]'
check "5.3 support stacks re-deployed with COMPONENTS=all" '[ "$(grep -c "COMPONENTS=all" "$STUB_STATE/calls.log")" -ge 3 ]'

run "${SANDBOX_DIR}/deploy.sh" --revision 2
check "6.1 update v1->v2 exit 0" '[ "$RC" -eq 0 ]'
check "6.2 manifest has v2" 'python3 -c "import json;d=json.load(open(\"$MANIFEST\"));assert d[\"versions\"][\"v2\"][\"blazor\"]" 2>/dev/null'
check "6.3 v2 app deployed" '[ -f "$STUB_STATE/stacks/'"$APP_STACK"'.json" ]'

run "${SANDBOX_DIR}/deploy.sh" --revision 2 --second-distribution
check "7.1 enable second distribution exit 0" '[ "$RC" -eq 0 ]'
check "7.2 SECOND=1 seen" 'grep -q "SECOND=1" "$STUB_STATE/calls.log"'
run "${SANDBOX_DIR}/deploy.sh" --revision 2
check "7.3 disable second distribution exit 0" '[ "$RC" -eq 0 ]'
check "7.4 SECOND=0 seen" 'grep -q "SECOND=0" "$STUB_STATE/calls.log"'

run "${SANDBOX_DIR}/destroy.sh" --yes
check "8.1 destroy exit 0" '[ "$RC" -eq 0 ]'
check "8.2 no stacks remain" '[ -z "$(ls -A "$STUB_STATE/stacks" 2>/dev/null)" ]'
check "8.3 no buckets remain" '[ -z "$(ls -A "$STUB_STATE/buckets" 2>/dev/null)" ]'
check "8.4 no log groups remain" '[ ! -e "$STUB_STATE/logs/aws/lambda/web/wfsbx-nextjs" ]'
check "8.5 manifest removed" '[ ! -f "$MANIFEST" ]'
check "8.6 never called s3api delete-bucket (M2)" '[ ! -f "$STUB_STATE/delete_bucket_called" ]'

# --------------------------------------------------------------------------------------------------
scenario "9-10: destroy after partial deploy + repeated destroy"
fresh_state
cleanup_manifest
run "${SANDBOX_DIR}/deploy.sh" --stacks-only
check "9.1 partial deploy exit 0" '[ "$RC" -eq 0 ]'
run "${SANDBOX_DIR}/destroy.sh" --yes
check "9.2 destroy partial exit 0" '[ "$RC" -eq 0 ]'
check "9.3 stacks gone" '[ -z "$(ls -A "$STUB_STATE/stacks" 2>/dev/null)" ]'
check "9.4 no delete-bucket" '[ ! -f "$STUB_STATE/delete_bucket_called" ]'
run "${SANDBOX_DIR}/destroy.sh" --yes
check "10.1 repeated destroy exit 0" '[ "$RC" -eq 0 ]'

# --------------------------------------------------------------------------------------------------
scenario "11: account mismatch fails closed"
fresh_state
cleanup_manifest
OUT="$(SANDBOX_ACCOUNT=999999999999 "${SANDBOX_DIR}/deploy.sh" --stacks-only 2>&1)"
RC=$?
check "11.1 exit non-zero" '[ "$RC" -ne 0 ]'
check "11.2 reports mismatch" 'printf "%s" "$OUT" | grep -q "does not match SANDBOX_ACCOUNT"'
check "11.3 no stacks created" '[ -z "$(ls -A "$STUB_STATE/stacks" 2>/dev/null)" ]'

# --------------------------------------------------------------------------------------------------
scenario "12: foreign pre-existing stack refused"
fresh_state
cleanup_manifest
mkdir -p "$STUB_STATE/stacks"
printf '%s' '{"Tags":[{"Key":"lc-webfunction-sandbox-id","Value":"wfsbx"},{"Key":"lc-webfunction-sandbox-owner","Value":"SomeOtherProject"}],"Outputs":[],"Kind":"artifacts"}' >"$STUB_STATE/stacks/${A_STACK}.json"
run "${SANDBOX_DIR}/deploy.sh" --stacks-only
check "12.1 exit non-zero" '[ "$RC" -ne 0 ]'
check "12.2 reports not owned" 'printf "%s" "$OUT" | grep -q "not owned"'

# --------------------------------------------------------------------------------------------------
scenario "13: credential / API failures fail closed"
fresh_state
cleanup_manifest
OUT="$(STUB_FAIL=sts "${SANDBOX_DIR}/deploy.sh" --stacks-only 2>&1)"; RC=$?
check "13.1 sts failure exit non-zero" '[ "$RC" -ne 0 ]'
check "13.2 sts failure reported" 'printf "%s" "$OUT" | grep -q "get-caller-identity failed"'
fresh_state
OUT="$(STUB_FAIL=describe "${SANDBOX_DIR}/deploy.sh" --stacks-only 2>&1)"; RC=$?
check "13.3 describe failure exit non-zero" '[ "$RC" -ne 0 ]'
check "13.4 ownership ambiguity fatal" 'printf "%s" "$OUT" | grep -q "fail closed"'
check "13.5 no stacks created" '[ -z "$(ls -A "$STUB_STATE/stacks" 2>/dev/null)" ]'

# --------------------------------------------------------------------------------------------------
scenario "14: S3 cleanup failure aborts destroy and preserves state"
fresh_state
cleanup_manifest
run "${SANDBOX_DIR}/deploy.sh" --stacks-only
run "${SANDBOX_DIR}/upload-artifacts.sh" --revision 1
check "14.1 preconditions ok" '[ "$RC" -eq 0 ] && [ -f "$MANIFEST" ]'
OUT="$(STUB_FAIL=s3list "${SANDBOX_DIR}/destroy.sh" --yes 2>&1)"; RC=$?
check "14.2 destroy aborted" '[ "$RC" -ne 0 ]'
check "14.3 external stack preserved" '[ -f "$STUB_STATE/stacks/'"$E_STACK"'.json" ]'
check "14.4 artifacts stack preserved" '[ -f "$STUB_STATE/stacks/'"$A_STACK"'.json" ]'
check "14.5 manifest preserved" '[ -f "$MANIFEST" ]'
check "14.6 no delete-bucket" '[ ! -f "$STUB_STATE/delete_bucket_called" ]'

# --------------------------------------------------------------------------------------------------
scenario "15: manifest missing / mismatched"
fresh_state
cleanup_manifest
run "${SANDBOX_DIR}/deploy.sh" --stacks-only
OUT="$("${SANDBOX_DIR}/deploy.sh" --revision 1 --skip-upload 2>&1)"; RC=$?
check "15.1 skip-upload without manifest fails" '[ "$RC" -ne 0 ]'
check "15.2 reports missing manifest" 'printf "%s" "$OUT" | grep -qi "manifest"'
mkdir -p "$(dirname "$MANIFEST")"
printf '%s' '{"sandboxId":"other","account":"123456789012","region":"us-east-1","prefix":"other","artifactsBucket":"x","externalBucket":"y","versions":{"v1":{"nextjs":"1","aspnetcore":"1","blazor":"1"}}}' >"$MANIFEST"
OUT="$("${SANDBOX_DIR}/deploy.sh" --revision 1 --skip-upload 2>&1)"; RC=$?
check "15.3 foreign manifest refused" '[ "$RC" -ne 0 ]'
check "15.4 reports manifest mismatch" 'printf "%s" "$OUT" | grep -qi "does not match SANDBOX_ID"'
cleanup_manifest

# --------------------------------------------------------------------------------------------------
scenario "16: non-default artifact prefix is consistent"
fresh_state
cleanup_manifest
export SANDBOX_ARTIFACTS_PREFIX="altpre"
run "${SANDBOX_DIR}/deploy.sh" --stacks-only
run "${SANDBOX_DIR}/upload-artifacts.sh" --revision 1
check "16.1 upload exit 0" '[ "$RC" -eq 0 ]'
check "16.2 manifest prefix is altpre" 'python3 -c "import json;d=json.load(open(\"$MANIFEST\"));assert d[\"prefix\"]==\"altpre\"" 2>/dev/null'
check "16.3 object key uses prefix" 'grep -q -- "--key altpre/nextjs.zip" "$STUB_STATE/calls.log"'
run "${SANDBOX_DIR}/deploy.sh" --revision 1 --skip-upload
check "16.4 redeploy with prefix exit 0" '[ "$RC" -eq 0 ]'
unset SANDBOX_ARTIFACTS_PREFIX
cleanup_manifest

# --------------------------------------------------------------------------------------------------
# Authorization-gate regression coverage (shared-account acknowledgement semantics).
# --------------------------------------------------------------------------------------------------
scenario "17: mutations require the shared-account acknowledgement"
fresh_state
cleanup_manifest
OUT="$(env -u SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT "${SANDBOX_DIR}/deploy.sh" --stacks-only 2>&1)"; RC=$?
check "17.1 exit non-zero" '[ "$RC" -ne 0 ]'
check "17.2 names the shared-account flag" 'printf "%s" "$OUT" | grep -q "SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1"'
check "17.3 no stacks created" '[ -z "$(ls -A "$STUB_STATE/stacks" 2>/dev/null)" ]'

scenario "18: mutations require the AWS opt-in even with the shared-account acknowledgement"
fresh_state
cleanup_manifest
OUT="$(env -u SANDBOX_ALLOW_AWS "${SANDBOX_DIR}/deploy.sh" --stacks-only 2>&1)"; RC=$?
check "18.1 exit non-zero" '[ "$RC" -ne 0 ]'
check "18.2 names the opt-in flag" 'printf "%s" "$OUT" | grep -q "SANDBOX_ALLOW_AWS=1"'
check "18.3 no stacks created" '[ -z "$(ls -A "$STUB_STATE/stacks" 2>/dev/null)" ]'

scenario "19: wrong account and wrong Region both fail closed"
fresh_state
cleanup_manifest
OUT="$(SANDBOX_ACCOUNT=999999999999 "${SANDBOX_DIR}/deploy.sh" --stacks-only 2>&1)"; RC=$?
check "19.1 wrong account exit non-zero" '[ "$RC" -ne 0 ]'
check "19.2 reports account mismatch" 'printf "%s" "$OUT" | grep -q "does not match SANDBOX_ACCOUNT"'
check "19.3 no stacks created" '[ -z "$(ls -A "$STUB_STATE/stacks" 2>/dev/null)" ]'
OUT="$(AWS_REGION=eu-west-1 "${SANDBOX_DIR}/deploy.sh" --stacks-only 2>&1)"; RC=$?
check "19.4 wrong Region exit non-zero" '[ "$RC" -ne 0 ]'
check "19.5 reports Region mismatch" 'printf "%s" "$OUT" | grep -q "does not match SANDBOX_REGION"'
check "19.6 no stacks created" '[ -z "$(ls -A "$STUB_STATE/stacks" 2>/dev/null)" ]'

scenario "20: a foreign pre-existing stack cannot be modified"
fresh_state
cleanup_manifest
mkdir -p "$STUB_STATE/stacks"
printf '%s' '{"Tags":[{"Key":"lc-webfunction-sandbox-id","Value":"wfsbx"},{"Key":"lc-webfunction-sandbox-owner","Value":"SomeOtherProject"}],"Outputs":[],"Kind":"artifacts"}' >"$STUB_STATE/stacks/${A_STACK}.json"
foreign_before="$(cat "$STUB_STATE/stacks/${A_STACK}.json")"
OUT="$("${SANDBOX_DIR}/deploy.sh" --stacks-only 2>&1)"; RC=$?
check "20.1 exit non-zero" '[ "$RC" -ne 0 ]'
check "20.2 reports not owned" 'printf "%s" "$OUT" | grep -q "not owned"'
check "20.3 foreign stack left untouched" '[ "$(cat "$STUB_STATE/stacks/${A_STACK}.json")" = "$foreign_before" ]'
check "20.4 no deploy was attempted" '! grep -q "deploy" "$STUB_STATE/calls.log" 2>/dev/null'

scenario "21: a foreign stack cannot be destroyed"
fresh_state
cleanup_manifest
mkdir -p "$STUB_STATE/stacks"
printf '%s' '{"Tags":[{"Key":"lc-webfunction-sandbox-id","Value":"wfsbx"},{"Key":"lc-webfunction-sandbox-owner","Value":"SomeOtherProject"}],"Outputs":[],"Kind":"artifacts"}' >"$STUB_STATE/stacks/${A_STACK}.json"
OUT="$("${SANDBOX_DIR}/destroy.sh" --yes 2>&1)"; RC=$?
check "21.1 exit non-zero" '[ "$RC" -ne 0 ]'
check "21.2 reports not owned" 'printf "%s" "$OUT" | grep -qi "not owned"'
check "21.3 foreign stack preserved" '[ -f "$STUB_STATE/stacks/'"$A_STACK"'.json" ]'

scenario "22: the obsolete non-production acknowledgement authorizes nothing"
fresh_state
cleanup_manifest
OUT="$(env -u SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT SANDBOX_ACKNOWLEDGE_NON_PRODUCTION=1 "${SANDBOX_DIR}/deploy.sh" --stacks-only 2>&1)"; RC=$?
check "22.1 exit non-zero" '[ "$RC" -ne 0 ]'
check "22.2 still requires the shared-account flag" 'printf "%s" "$OUT" | grep -q "SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1"'
check "22.3 no stacks created" '[ -z "$(ls -A "$STUB_STATE/stacks" 2>/dev/null)" ]'

scenario "23: the shared-account acknowledgement is scoped to the verified account"
fresh_state
cleanup_manifest
run "${SANDBOX_DIR}/deploy.sh" --stacks-only
check "23.1 verified target with acknowledgement succeeds" '[ "$RC" -eq 0 ]'
fresh_state
cleanup_manifest
OUT="$(SANDBOX_ACCOUNT=999999999999 "${SANDBOX_DIR}/deploy.sh" --stacks-only 2>&1)"; RC=$?
check "23.2 acknowledgement does not authorize a different account" '[ "$RC" -ne 0 ]'
check "23.3 reports account mismatch" 'printf "%s" "$OUT" | grep -q "does not match SANDBOX_ACCOUNT"'
check "23.4 no stacks created" '[ -z "$(ls -A "$STUB_STATE/stacks" 2>/dev/null)" ]'

scenario "24: destroy still requires its own separate confirmation"
fresh_state
cleanup_manifest
run "${SANDBOX_DIR}/deploy.sh" --stacks-only
check "24.1 preconditions ok" '[ "$RC" -eq 0 ] && [ -f "$STUB_STATE/stacks/'"$A_STACK"'.json" ]'
OUT="$(env -u SANDBOX_CONFIRM_DESTROY "${SANDBOX_DIR}/destroy.sh" --yes 2>&1)"; RC=$?
check "24.2 destroy without confirmation fails" '[ "$RC" -ne 0 ]'
check "24.3 stack preserved" '[ -f "$STUB_STATE/stacks/'"$A_STACK"'.json" ]'
OUT="$(SANDBOX_CONFIRM_DESTROY=wrong "${SANDBOX_DIR}/destroy.sh" --yes 2>&1)"; RC=$?
check "24.4 destroy with wrong confirmation fails" '[ "$RC" -ne 0 ]'
check "24.5 stack still preserved" '[ -f "$STUB_STATE/stacks/'"$A_STACK"'.json" ]'
run "${SANDBOX_DIR}/destroy.sh" --yes
check "24.6 destroy with confirmation succeeds" '[ "$RC" -eq 0 ]'
check "24.7 stacks gone" '[ -z "$(ls -A "$STUB_STATE/stacks" 2>/dev/null)" ]'
cleanup_manifest

# --------------------------------------------------------------------------------------------------
printf '\n==================================================\n'
printf 'Offline lifecycle scenarios: %d passed, %d failed\n' "$PASS" "$FAIL"
if [ "$FAIL" -ne 0 ]; then
    printf 'Failures:\n'
    for f in ${FAILURES[@]+"${FAILURES[@]}"}; do printf '  - %s\n' "$f"; done
    exit 1
fi
