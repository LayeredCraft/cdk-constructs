#!/usr/bin/env bash
#
# smoke.sh - T7c acceptance harness for the Web Function sandbox (design section 19.1, items 3.1-3.8).
#
# Read-only: it never mutates AWS. It observes deployed behaviour through the AWS APIs, CloudFront and
# the endpoint data plane, and writes a machine-readable summary to build/smoke-summary.json.
#
# Usage:
#   SANDBOX_ALLOW_AWS=1 SANDBOX_ID=wfsbx SANDBOX_ACCOUNT=123456789012 SANDBOX_REGION=us-east-1 \
#       ./smoke.sh [--second-distribution] [--expect-build-id <value>] \
#                  [--revision-baseline <file>] [--attempts <n>] [--no-retry]
#
# Flags:
#   --second-distribution      expect the optional second CloudFront distribution on the Next.js endpoint
#   --expect-build-id <value>  assert the workload build id equals <value> (v1 or v2)
#   --revision-baseline <file> compare per-function revisions against a previously captured summary
#   --attempts <n>             retry attempts for eventual-consistency checks (1-60, default 8)
#   --no-retry                 disable retries (equivalent to --attempts 1)
#
# Checks that need a live API that is unavailable in the installed AWS CLI or Region are recorded as
# BLOCKED with the missing API named - they are never reported as PASS.

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

second_distribution=0
expect_build_id=""
revision_baseline=""
max_attempts="${SMOKE_ATTEMPTS:-8}"
retry=1

while [ "$#" -gt 0 ]; do
    case "$1" in
        --second-distribution)
            second_distribution=1
            shift
            ;;
        --expect-build-id)
            [ "$#" -ge 2 ] || die "--expect-build-id requires a value."
            expect_build_id="$2"
            shift 2
            ;;
        --revision-baseline)
            [ "$#" -ge 2 ] || die "--revision-baseline requires a file path."
            revision_baseline="$2"
            shift 2
            ;;
        --attempts)
            [ "$#" -ge 2 ] || die "--attempts requires a value."
            max_attempts="$2"
            shift 2
            ;;
        --no-retry)
            retry=0
            shift
            ;;
        -h | --help)
            sed -n '2,22p' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        *) die "unknown argument: $1" ;;
    esac
done

case "${max_attempts}" in
    '' | *[!0-9]*) die "--attempts must be a positive integer." ;;
esac
[ "${max_attempts}" -ge 1 ] || die "--attempts must be at least 1."
[ "${max_attempts}" -le 60 ] || die "--attempts must be at most 60 (retries are bounded)."
[ "${retry}" -eq 1 ] || max_attempts=1

validate_sandbox_env
require_aws_allowed
verify_aws_identity
require_cmd aws
require_cmd curl
require_cmd python3

app_stack="$(stack_name_app)"
artifacts_bucket="$(bucket_artifacts)"
external_bucket="$(bucket_external)"
prefix="$(object_prefix)"

# Refuse to smoke-test a stack that is not provably owned by this sandbox.
app_ownership="$(stack_ownership "${app_stack}")"
case "${app_ownership}" in
    owned) ;;
    absent) die "app stack ${app_stack} does not exist; deploy the sandbox before running smoke tests." ;;
    foreign:*) die "app stack ${app_stack} is not owned by sandbox '${SANDBOX_ID}' (${app_ownership#foreign:}); refusing to smoke-test it." ;;
    error:*) die "could not determine ownership of app stack ${app_stack}; refusing to continue (fail closed)." ;;
    *) die "unexpected ownership result for ${app_stack}: ${app_ownership}" ;;
esac

SMOKE_TMP="${SANDBOX_DIR}/build/.smoke"
mkdir -p "${SMOKE_TMP}"
SMOKE_BODY="${SMOKE_TMP}/body"
SMOKE_ERR="${SMOKE_TMP}/err"
SMOKE_CURL_CONFIG="${SMOKE_TMP}/curl-auth.conf"

# --- Outputs -------------------------------------------------------------------------------------

output() {
    local value
    value="$(stack_output "${app_stack}" "$1")" ||
        die "app stack is missing output '$1'."
    [ -n "${value}" ] || die "app stack output '$1' is empty."
    printf '%s' "${value}"
}

sandbox_id_out="$(output SandboxId)"
sandbox_account_out="$(output SandboxAccount)"
sandbox_region_out="$(output SandboxRegion)"
[ "${sandbox_id_out}" = "${SANDBOX_ID}" ] || die "app stack SandboxId '${sandbox_id_out}' does not match SANDBOX_ID '${SANDBOX_ID}'."
[ "${sandbox_account_out}" = "${SANDBOX_ACCOUNT}" ] || die "app stack SandboxAccount '${sandbox_account_out}' does not match SANDBOX_ACCOUNT '${SANDBOX_ACCOUNT}'."
[ "${sandbox_region_out}" = "${SANDBOX_REGION}" ] || die "app stack SandboxRegion '${sandbox_region_out}' does not match SANDBOX_REGION '${SANDBOX_REGION}'."

nextjs_function="$(output NextJsFunctionName)"
nextjs_dist_domain="$(output NextJsDistributionDomain)"
nextjs_dist_id="$(output NextJsDistributionId)"
nextjs_endpoint_domain="$(output NextJsEndpointDomain)"
nextjs_endpoint_arn="$(output NextJsEndpointArn)"
nextjs_second_dist_domain=""
nextjs_second_dist_id=""
if [ "${second_distribution}" = "1" ]; then
    nextjs_second_dist_domain="$(output NextJsSecondDistributionDomain)"
    nextjs_second_dist_id="$(output NextJsSecondDistributionId)"
fi

aspnetcore_function="$(output AspNetCoreFunctionName)"
aspnetcore_url="$(output AspNetCoreEndpointUrl)"
aspnetcore_alt_url="$(output AspNetCoreAltEndpointUrl)"
aspnetcore_endpoint_arn="$(output AspNetCoreEndpointArn)"

blazor_function="$(output BlazorFunctionName)"
blazor_dist_domain="$(output BlazorDistributionDomain)"
blazor_dist_id="$(output BlazorDistributionId)"
blazor_endpoint_arn="$(output BlazorEndpointArn)"

caller_role_arn="$(output CallerRoleArn)"
denied_role_arn="$(output DeniedRoleArn)"
imported_immutable_arn="$(output ImportedCallerImmutableRoleArn)"
imported_mutable_arn="$(output ImportedCallerMutableRoleArn)"

# CloudFront distribution ARNs use an empty Region component (CloudFront is a global service).
nextjs_dist_arn="arn:aws:cloudfront::${SANDBOX_ACCOUNT}:distribution/${nextjs_dist_id}"
blazor_dist_arn="arn:aws:cloudfront::${SANDBOX_ACCOUNT}:distribution/${blazor_dist_id}"
nextjs_second_dist_arn=""
[ "${second_distribution}" = "1" ] &&
    nextjs_second_dist_arn="arn:aws:cloudfront::${SANDBOX_ACCOUNT}:distribution/${nextjs_second_dist_id}"

# --- Result framework ----------------------------------------------------------------------------

declare -a RESULTS=()
pass_count=0
fail_count=0
skip_count=0
blocked_count=0
SUMMARY_WRITTEN=0
TEST_PHASE=0

record() {
    local status="$1" name="$2" detail="${3:-}"
    case "${status}" in
        PASS) pass_count=$((pass_count + 1)) ;;
        FAIL) fail_count=$((fail_count + 1)) ;;
        SKIP) skip_count=$((skip_count + 1)) ;;
        BLOCKED) blocked_count=$((blocked_count + 1)) ;;
        *) die "internal error: unknown result status '${status}'" ;;
    esac
    RESULTS+=("${status}|${name}|${detail}")
    if [ -n "${detail}" ]; then
        printf '[%s] %s - %s\n' "${status}" "${name}" "${detail}" >&2
    else
        printf '[%s] %s\n' "${status}" "${name}" >&2
    fi
}

settle() {
    local name="$1" expected="$2" actual="$3" note="${4:-}"
    if [ "${expected}" = "${actual}" ]; then
        record PASS "${name}" "${note:+${note} }observed=${actual}"
    else
        record FAIL "${name}" "expected ${expected}, got ${actual}${note:+ (${note})}"
    fi
}

# --- HTTP helpers --------------------------------------------------------------------------------

fetch() {
    local url="$1"
    shift
    local code
    code="$(curl --silent --show-error --location --max-time 30 \
        --output "${SMOKE_BODY}" --write-out '%{http_code}' "$@" "${url}" 2>"${SMOKE_ERR}")" || true
    [ -n "${code}" ] || code="000"
    printf '%s' "${code}"
}

fetch_timed() {
    local url="$1"
    shift
    local out
    out="$(curl --silent --show-error --location --max-time 30 \
        --output "${SMOKE_BODY}" --write-out '%{http_code} %{time_total}' "$@" "${url}" 2>"${SMOKE_ERR}")" || true
    printf '%s' "${out:-000 0}"
}

body_contains() { grep -q -- "$1" "${SMOKE_BODY}" 2>/dev/null; }

fetch_until() {
    local name="$1" expected="$2" url="$3" note="${4:-}"
    if [ "$#" -ge 4 ]; then shift 4; else shift "$#"; fi
    local attempt=1 code="000"
    while [ "${attempt}" -le "${max_attempts}" ]; do
        code="$(fetch "${url}" "$@")"
        if [ "${code}" = "${expected}" ]; then
            record PASS "${name}" "${note:+${note} }http=${code}"
            return 0
        fi
        attempt=$((attempt + 1))
        [ "${attempt}" -le "${max_attempts}" ] && sleep 5
    done
    record FAIL "${name}" "expected http ${expected}, got ${code} after ${max_attempts} attempt(s)${note:+ (${note})}"
    return 0
}

# Distinguishes a genuine access-denial from a signature/credential problem.
classify_denial() {
    local signed="$1" blob
    blob="$(cat "${SMOKE_BODY}" 2>/dev/null; cat "${SMOKE_ERR}" 2>/dev/null)"
    case "${blob}" in
        *SignatureDoesNotMatch* | *InvalidSignatureException* | *"expired"* | *ExpiredToken* | *InvalidClientTokenId*)
            printf 'signature' ;;
        *AccessDeniedException* | *"not authorized"* | *Forbidden* | *"Missing Authentication Token"* | *UnauthorizedOperation*)
            printf 'access-denied' ;;
        *)
            if [ "${signed}" = "0" ]; then printf 'access-denied'; else printf 'unrecognized'; fi
            ;;
    esac
}

assert_denied() {
    local name="$1" status="$2" signed="$3" note="${4:-}"
    if [ "${status}" != "403" ]; then
        record FAIL "${name}" "expected 403, got ${status}${note:+ (${note})}"
        return 0
    fi
    case "$(classify_denial "${signed}")" in
        access-denied) record PASS "${name}" "${note:+${note} }403 access denied" ;;
        signature) record FAIL "${name}" "403 was a signature/credential failure, not an authorization denial${note:+ (${note})}" ;;
        *) record FAIL "${name}" "403 could not be attributed to an authorization denial${note:+ (${note})}" ;;
    esac
}

# --- IAM signing ---------------------------------------------------------------------------------

SMOKE_AK=""
SMOKE_SK=""
SMOKE_TOKEN=""
CURL_AUTH=()

assume() {
    local role_arn="$1" json
    json="$(aws_cmd sts assume-role --role-arn "${role_arn}" \
        --role-session-name sandbox-smoke --output json 2>"${SMOKE_ERR}")" || return 1
    SMOKE_AK="$(printf '%s' "${json}" | python3 -c 'import json,sys; print(json.load(sys.stdin)["Credentials"]["AccessKeyId"])')" || return 1
    SMOKE_SK="$(printf '%s' "${json}" | python3 -c 'import json,sys; print(json.load(sys.stdin)["Credentials"]["SecretAccessKey"])')" || return 1
    SMOKE_TOKEN="$(printf '%s' "${json}" | python3 -c 'import json,sys; print(json.load(sys.stdin)["Credentials"]["SessionToken"])')" || return 1
    return 0
}

# Writes a 0600 curl config so credentials never appear on the process command line.
sigv4_args() {
    umask 077
    {
        printf 'aws-sigv4 = "aws:amz:%s:lambda"\n' "${SANDBOX_REGION}"
        printf 'user = "%s:%s"\n' "${SMOKE_AK}" "${SMOKE_SK}"
        printf 'header = "x-amz-content-sha256: UNSIGNED-PAYLOAD"\n'
        [ -n "${SMOKE_TOKEN}" ] && printf 'header = "x-amz-security-token: %s"\n' "${SMOKE_TOKEN}"
    } > "${SMOKE_CURL_CONFIG}"
    chmod 600 "${SMOKE_CURL_CONFIG}"
    CURL_AUTH=(-K "${SMOKE_CURL_CONFIG}")
}

# --- IAM policy inspection -----------------------------------------------------------------------

# Prints one of: none | invoke | broad | error.
# A lookup failure is NEVER treated as "no policy".
role_invoke_state() {
    local role_arn="$1" role_name inline_json attached_json
    role_name="${role_arn##*/}"
    inline_json="$(aws_cmd iam list-role-policies --role-name "${role_name}" \
        --query 'PolicyNames' --output json 2>/dev/null)" || { printf 'error'; return 0; }
    attached_json="$(aws_cmd iam list-attached-role-policies --role-name "${role_name}" \
        --query 'AttachedPolicies[].PolicyArn' --output json 2>/dev/null)" || { printf 'error'; return 0; }

    python3 - "${role_name}" "${inline_json}" "${attached_json}" "${SMOKE_TMP}" <<'PY'
import json, subprocess, sys

role_name, inline_json, attached_json, tmp = sys.argv[1:5]

def aws(*args):
    return subprocess.run(["aws", "iam", *args], capture_output=True, text=True)

docs = []
try:
    for name in json.loads(inline_json or "[]"):
        out = aws("get-role-policy", "--role-name", role_name, "--policy-name", name, "--output", "json")
        if out.returncode != 0:
            print("error"); sys.exit(0)
        docs.append(json.loads(out.stdout).get("PolicyDocument", {}))
    for arn in json.loads(attached_json or "[]"):
        meta = aws("get-policy", "--policy-arn", arn, "--output", "json")
        if meta.returncode != 0:
            print("error"); sys.exit(0)
        ver = json.loads(meta.stdout)["Policy"].get("DefaultVersionId")
        doc = aws("get-policy-version", "--policy-arn", arn, "--version-id", ver, "--output", "json")
        if doc.returncode != 0:
            print("error"); sys.exit(0)
        docs.append(json.loads(doc.stdout)["PolicyVersion"]["Document"])
except Exception:
    print("error"); sys.exit(0)

def stmts(doc):
    if isinstance(doc, dict) and "Statement" in doc:
        s = doc["Statement"]
        return s if isinstance(s, list) else [s]
    return []

result = "none"
for doc in docs:
    for st in stmts(doc):
        if st.get("Effect") != "Allow":
            continue
        actions = st.get("Action")
        actions = actions if isinstance(actions, list) else [actions]
        for a in actions:
            if a in ("*", "lambda:*"):
                result = "broad"
            elif a == "lambda:InvokeWebFunctionEndpoint" and result != "broad":
                result = "invoke"
print(result)
PY
}

# --- Live resource policy ------------------------------------------------------------------------

# The resource policy is attached to the Web Function ARN (not the endpoint), so get-resource-policy
# needs the function ARN: the endpoint ARN without its trailing /endpoint/<name> segment.
web_function_arn_from_endpoint() {
    printf '%s' "${1%/endpoint/*}"
}

live_policy_for() {
    local arn="$1"
    aws_cmd lambda-web get-resource-policy --resource-arn "${arn}" \
        --query 'Policy' --output text 2>/dev/null || true
}

# Validates live resource policies. Emits exactly one result line per expected check, in the form
# STATUS|NAME|DETAIL. Parsing/schema failures and missing observations become FAIL/BLOCKED - never a
# silent omission and never a substitute for the live observation.
policy_report() {
    python3 - "${SMOKE_TMP}/policy-input.json" <<'PY'
import json, sys

path = sys.argv[1]
lines = []

def emit(status, name, detail):
    lines.append("%s|%s|%s" % (status, name, detail.replace("\n", " ")))

try:
    payload = json.load(open(path))
except Exception as exc:
    emit("FAIL", "resource policy verification", "could not read policy input: %s" % exc)
    sys.stdout.write("\n".join(lines))
    sys.exit(0)

def statements(policy_text):
    if not policy_text or policy_text.strip() in ("", "None"):
        return None
    doc = json.loads(policy_text)
    if isinstance(doc, dict) and "Statement" in doc:
        st = doc["Statement"]
        return st if isinstance(st, list) else [st]
    raise ValueError("policy has no Statement array")

def action_of(st):
    a = st.get("Action")
    return a if isinstance(a, list) else [a]

for check in payload["checks"]:
    name = check["name"]
    policy = payload["policies"].get(check["key"], "")
    try:
        sts = statements(policy)
    except Exception as exc:
        emit("FAIL", name, "live resource policy is unreadable (%s)" % exc)
        continue
    if sts is None:
        emit("BLOCKED", name, "live resource policy unavailable (aws lambda-web get-resource-policy returned nothing)")
        continue

    allow = []
    for st in sts:
        if st.get("Effect") == "Allow" and "lambda:InvokeWebFunctionEndpoint" in action_of(st):
            allow.append(st)

    if check["kind"] == "cloudfront":
        matched = {}
        unexpected = 0
        for st in allow:
            principal = st.get("Principal")
            svc = principal.get("Service") if isinstance(principal, dict) else None
            svc = svc if isinstance(svc, list) else [svc]
            if "cloudfront.amazonaws.com" not in svc:
                continue
            resource = st.get("Resource")
            resource = resource if isinstance(resource, list) else [resource]
            if check["endpoint_arn"] not in resource:
                emit("FAIL", name, "statement does not target the expected endpoint ARN")
                allow = None
                break
            cond = st.get("Condition", {})
            source = cond.get("StringEquals", {}).get("aws:SourceArn")
            source = source if isinstance(source, list) else [source]
            for s in source:
                matched[s] = matched.get(s, 0) + 1
        if allow is None:
            continue
        expected = check["dist_arns"]
        problems = []
        for arn in expected:
            n = matched.get(arn, 0)
            if n != 1:
                problems.append("%s matched %d time(s)" % (arn, n))
        extra = [a for a in matched if a not in expected]
        if extra:
            problems.append("unexpected distribution grant(s): %s" % ", ".join(sorted(extra)))
        if problems:
            emit("FAIL", name, "; ".join(problems))
        else:
            emit("PASS", name, "%d CloudFront statement(s) with correct source ARN(s)" % len(expected))
    else:  # roles
        principals = set()
        for st in allow:
            principal = st.get("Principal")
            aws_principals = principal.get("AWS") if isinstance(principal, dict) else None
            aws_principals = aws_principals if isinstance(aws_principals, list) else [aws_principals]
            for p in aws_principals:
                if p:
                    principals.add(p)
            resource = st.get("Resource")
            resource = resource if isinstance(resource, list) else [resource]
            if check["endpoint_arn"] not in resource:
                emit("FAIL", name, "statement does not target the expected endpoint ARN")
                principals = None
                break
        if principals is None:
            continue
        expected = set(check["principals"])
        if principals != expected:
            emit("FAIL", name, "principals %s do not match expected %s" % (sorted(principals), sorted(expected)))
        else:
            emit("PASS", name, "%d role principal(s) with correct endpoint resource" % len(expected))

sys.stdout.write("\n".join(lines))
PY
}

# --- Revision tracking ---------------------------------------------------------------------------

# Prints the revision the default endpoint routes to, or exits non-zero:
#   1 = API/command unavailable, 3 = revision identity missing or not Active.
# The CLI 2.38.x get-web-function response carries no revision, so the routing identity comes from the
# default endpoint's revision weights and is corroborated by list-web-function-revisions (Active).
web_function_revision() {
    local fn="$1" ep_json list_json
    ep_json="$(aws_cmd lambda-web get-web-function-endpoint --function-name "${fn}" \
        --endpoint-name default --output json 2>/dev/null)" || return 1
    printf '%s' "${ep_json}" > "${SMOKE_TMP}/revision-endpoint.json"
    list_json="$(aws_cmd lambda-web list-web-function-revisions --function-name "${fn}" \
        --output json 2>/dev/null)" || return 1
    printf '%s' "${list_json}" > "${SMOKE_TMP}/revision-list.json"
    python3 - "${SMOKE_TMP}/revision-endpoint.json" "${SMOKE_TMP}/revision-list.json" <<'PY'
import json, sys

def field(doc, *keys):
    for key in keys:
        if isinstance(doc, dict) and doc.get(key):
            return doc[key]
    return None

try:
    endpoint = json.load(open(sys.argv[1]))
    listing = json.load(open(sys.argv[2]))
except Exception:
    sys.exit(3)

state = field(endpoint, "State", "state")
weights = field(endpoint, "RevisionWeights", "revisionWeights") or []
revision = None
for weight in weights:
    if isinstance(weight, dict):
        revision = field(weight, "RevisionId", "revisionId")
        if revision:
            break
if not revision or (state and state != "Active"):
    sys.exit(3)

for item in field(listing, "Revisions", "revisions") or []:
    if isinstance(item, dict) and field(item, "RevisionId", "revisionId") == revision:
        if field(item, "State", "state") == "Active":
            print(revision)
            sys.exit(0)
        sys.exit(3)
sys.exit(3)
PY
}

capture_revision() {
    local label="$1" fn="$2" out rc
    # The assignment is guarded so errexit does not abort on the expected non-zero revision returns.
    if out="$(web_function_revision "${fn}")"; then
        rc=0
    else
        rc=$?
    fi
    case "${rc}" in
        0) printf '%s' "${out}" ;;
        1) printf 'BLOCKED:lambda-web get-web-function-endpoint API unavailable' ;;
        *) printf 'BLOCKED:revision identity not exposed by the endpoint response' ;;
    esac
}

# --- Summary -------------------------------------------------------------------------------------

write_summary() {
    [ "${SUMMARY_WRITTEN}" = "1" ] && return 0
    SUMMARY_WRITTEN=1
    rm -f "${SMOKE_CURL_CONFIG}" 2>/dev/null || true

    printf '\n===== smoke summary =====\n' >&2
    local line
    for line in ${RESULTS[@]+"${RESULTS[@]}"}; do
        printf '  %s\n' "${line//|/ }" >&2
    done
    printf 'PASS=%d FAIL=%d SKIP=%d BLOCKED=%d\n' \
        "${pass_count}" "${fail_count}" "${skip_count}" "${blocked_count}" >&2

    local results_file="${SMOKE_TMP}/results.txt"
    : > "${results_file}"
    local line
    for line in ${RESULTS[@]+"${RESULTS[@]}"}; do
        printf '%s\n' "${line}" >> "${results_file}"
    done

    python3 - "${SANDBOX_DIR}/build/smoke-summary.json" "${results_file}" \
        "${pass_count}" "${fail_count}" "${skip_count}" "${blocked_count}" \
        "${REV_nextjs:-}" "${REV_aspnetcore:-}" "${REV_blazor:-}" <<'PY'
import json, os, sys

out_path, results_path, p, f, s, b, nr, ar, br = sys.argv[1:11]
results = []
with open(results_path) as fh:
    for raw in fh:
        raw = raw.rstrip("\n")
        if not raw:
            continue
        parts = raw.split("|", 2)
        results.append({
            "status": parts[0],
            "name": parts[1] if len(parts) > 1 else "",
            "detail": parts[2] if len(parts) > 2 else "",
        })

summary = {
    "sandboxId": os.environ.get("SANDBOX_ID", ""),
    "pass": int(p),
    "fail": int(f),
    "skip": int(s),
    "blocked": int(b),
    "revisions": {"nextjs": nr, "aspnetcore": ar, "blazor": br},
    "results": results,
}
with open(out_path, "w") as fh:
    json.dump(summary, fh, indent=2)
PY

    return 0
}

on_exit() {
    write_summary || true
    if [ "${fail_count}" -gt 0 ] || [ "${blocked_count}" -gt 0 ]; then
        exit 1
    fi
    exit 0
}

# --- Checks --------------------------------------------------------------------------------------

set +e
TEST_PHASE=1
trap 'on_exit' EXIT

# 3.1 - artifact deployment
if [ "$(aws_cmd s3api get-bucket-versioning --bucket "${artifacts_bucket}" --query Status --output text 2>/dev/null)" = "Enabled" ]; then
    record PASS "artifacts bucket versioning" "Enabled"
else
    record FAIL "artifacts bucket versioning" "expected Enabled"
fi

if [ -f "${MANIFEST_FILE}" ]; then
    for label in v1 v2; do
        for workload in nextjs aspnetcore blazor; do
            key="$(manifest_field "versions.${label}.${workload}" 2>/dev/null)" || continue
            bucket="${artifacts_bucket}"
            [ "${workload}" = "nextjs" ] || bucket="${external_bucket}"
            if aws_cmd s3api head-object --bucket "${bucket}" --key "${prefix}/${workload}.zip" \
                --version-id "${key}" --expected-bucket-owner "${SANDBOX_ACCOUNT}" >/dev/null 2>&1; then
                record PASS "artifact ${label}/${workload} present" "version=${key}"
            else
                record FAIL "artifact ${label}/${workload} present" "version=${key} not found"
            fi
        done
    done
else
    record SKIP "artifact manifest" "no local manifest; skipping per-version object checks"
fi

external_policy="$(aws_cmd s3api get-bucket-policy --bucket "${external_bucket}" --query Policy --output text 2>/dev/null || true)"
if printf '%s' "${external_policy}" | grep -q 'lambda.amazonaws.com'; then
    record PASS "external bucket grants lambda read" "lambda.amazonaws.com present"
else
    record FAIL "external bucket grants lambda read" "lambda.amazonaws.com not found in bucket policy"
fi

# 3.2 - revision activation
for pair in "WebFunction:3" "WebFunctionRevision:3" "WebFunctionEndpoint:4"; do
    type="${pair%%:*}"
    expected="${pair##*:}"
    actual="$(aws_cmd cloudformation describe-stack-resources --stack-name "${app_stack}" \
        --query "StackResources[?ResourceType=='AWS::Lambda::${type}'].LogicalResourceId" \
        --output text 2>/dev/null | tr '\t' '\n' | grep -c . )"
    settle "${type} resource count" "${expected}" "${actual}" "AWS::Lambda::${type}"
done

REV_nextjs="$(capture_revision nextjs "${nextjs_function}")"
REV_aspnetcore="$(capture_revision aspnetcore "${aspnetcore_function}")"
REV_blazor="$(capture_revision blazor "${blazor_function}")"
for pair in "nextjs:${REV_nextjs}" "aspnetcore:${REV_aspnetcore}" "blazor:${REV_blazor}"; do
    label="${pair%%:*}"
    value="${pair#*:}"
    case "${value}" in
        BLOCKED:*) record BLOCKED "revision identity (${label})" "${value#BLOCKED:}" ;;
        *) record PASS "revision identity (${label})" "revision=${value}" ;;
    esac
done

if [ -n "${revision_baseline}" ]; then
    if [ ! -f "${revision_baseline}" ]; then
        record FAIL "v1 -> v2 revision transition" "baseline file ${revision_baseline} not found"
    else
        transition_result="$(NEXTJS_REV="${REV_nextjs}" ASPNETCORE_REV="${REV_aspnetcore}" BLAZOR_REV="${REV_blazor}" \
            python3 - "${revision_baseline}" <<'PY'
import json, os, sys
try:
    baseline = json.load(open(sys.argv[1])).get("revisions", {})
except Exception as exc:
    print("FAIL|v1 -> v2 revision transition|baseline unreadable (%s)" % exc)
    sys.exit(0)
current = {
    "nextjs": os.environ["NEXTJS_REV"],
    "aspnetcore": os.environ["ASPNETCORE_REV"],
    "blazor": os.environ["BLAZOR_REV"],
}
problems = []
blocked = []
for name, cur in current.items():
    base = baseline.get(name, "")
    if cur.startswith("BLOCKED:") or base.startswith("BLOCKED:") or not base:
        blocked.append(name)
    elif cur == base:
        problems.append("%s unchanged (%s)" % (name, cur))
if problems:
    print("FAIL|v1 -> v2 revision transition|%s" % "; ".join(problems))
elif blocked:
    print("BLOCKED|v1 -> v2 revision transition|no revision identity for %s" % ", ".join(blocked))
else:
    print("PASS|v1 -> v2 revision transition|all functions advanced")
PY
)"
        if [ -n "${transition_result}" ]; then
            record "${transition_result%%|*}" \
                "$(printf '%s' "${transition_result}" | cut -d'|' -f2)" \
                "$(printf '%s' "${transition_result}" | cut -d'|' -f3-)"
        else
            record FAIL "v1 -> v2 revision transition" "no transition result produced"
        fi
    fi
fi

# readiness + timing (ASP.NET Core endpoint)
if assume "${caller_role_arn}"; then
    sigv4_args
    cold_result="$(fetch_timed "${aspnetcore_url}/health" ${CURL_AUTH[@]+"${CURL_AUTH[@]}"})"
    cold_code="${cold_result%% *}"
    cold_time="${cold_result#* }"
    settle "cold request succeeded" "200" "${cold_code}" "time=${cold_time}s"
    if body_contains '"backend":"aspnetcore"'; then
        record PASS "cold response body" "backend=aspnetcore"
    else
        record FAIL "cold response body" "expected backend=aspnetcore"
    fi
    cold_build="$(python3 - "${SMOKE_BODY}" <<'PY'
import json, sys
try:
    print(json.load(open(sys.argv[1])).get("buildId", ""))
except Exception:
    print("")
PY
)"
    warm_result="$(fetch_timed "${aspnetcore_url}/health" ${CURL_AUTH[@]+"${CURL_AUTH[@]}"})"
    warm_code="${warm_result%% *}"
    warm_time="${warm_result#* }"
    settle "warm request succeeded" "200" "${warm_code}" "time=${warm_time}s"
else
    record FAIL "assume caller role for readiness" "could not assume ${caller_role_arn}"
fi

# 3.5 - CloudFront OAC invocation + unsigned direct denial
fetch_until "Next.js via CloudFront" "200" "https://${nextjs_dist_domain}/" "OAC origin"
if body_contains "Next.js Web Function Sample"; then
    record PASS "Next.js SSR heading" "present"
else
    record FAIL "Next.js SSR heading" "missing"
fi
if body_contains 'id="build-id"'; then
    record PASS "Next.js build-id element" "present"
else
    record FAIL "Next.js build-id element" "missing"
fi
nextjs_build="$(python3 - "${SMOKE_BODY}" <<'PY'
import re, sys
html = open(sys.argv[1], encoding="utf-8", errors="replace").read()
m = re.search(r'data-build-id="([^"]*)"', html)
print(m.group(1).strip() if m else "")
PY
)"
if [ -n "${nextjs_build}" ]; then
    record PASS "Next.js build id" "value=${nextjs_build}"
else
    record FAIL "Next.js build id" "could not extract data-build-id"
fi
if [ -n "${expect_build_id}" ]; then
    settle "Next.js build id matches expected" "${expect_build_id}" "${nextjs_build}"
fi
nextjs_asset="$(grep -o '/_next/static/[^"]*\.js' "${SMOKE_BODY}" 2>/dev/null | head -1)"
if [ -n "${nextjs_asset}" ]; then
    fetch_until "Next.js static chunk" "200" "https://${nextjs_dist_domain}${nextjs_asset}" "fingerprinted asset"
else
    record FAIL "Next.js static chunk" "no /_next/static/*.js reference in SSR output"
fi
fetch_until "Next.js public asset" "200" "https://${nextjs_dist_domain}/sample.txt" "public/ asset"

if [ "${second_distribution}" = "1" ]; then
    fetch_until "Next.js second distribution" "200" "https://${nextjs_second_dist_domain}/" "second OAC origin"
fi

fetch_until "Blazor via CloudFront" "200" "https://${blazor_dist_domain}/" "OAC origin"
if body_contains "Blazor Static SSR Web Function Sample"; then
    record PASS "Blazor SSR heading" "present"
else
    record FAIL "Blazor SSR heading" "missing"
fi
blazor_build="$(python3 - "${SMOKE_BODY}" <<'PY'
import re, sys
html = open(sys.argv[1], encoding="utf-8", errors="replace").read()
m = re.search(r'data-build-id="([^"]*)"', html)
print(m.group(1).strip() if m else "")
PY
)"
if [ -n "${blazor_build}" ]; then
    record PASS "Blazor build id" "value=${blazor_build}"
else
    record FAIL "Blazor build id" "could not extract data-build-id"
fi
if [ -n "${expect_build_id}" ]; then
    settle "Blazor build id matches expected" "${expect_build_id}" "${blazor_build}"
fi
fetch_until "Blazor stylesheet" "200" "https://${blazor_dist_domain}/app.css" "static asset"
if body_contains "#512bd4"; then
    record PASS "Blazor stylesheet content" "#512bd4 present"
else
    record FAIL "Blazor stylesheet content" "expected #512bd4"
fi
fetch_until "Blazor framework script" "200" "https://${blazor_dist_domain}/_framework/blazor.web.js" "static asset"

# unsigned direct access to an IamAuth endpoint must be denied
code="$(fetch "https://${nextjs_endpoint_domain}/")"
assert_denied "unsigned direct endpoint denied" "${code}" 0 "no CloudFront, no signature"

# 3.3 - IAM-authorized invocation
for role_pair in "caller:${caller_role_arn}" "imported-immutable:${imported_immutable_arn}" "imported-mutable:${imported_mutable_arn}"; do
    role_name="${role_pair%%:*}"
    role_arn="${role_pair#*:}"
    if assume "${role_arn}"; then
        sigv4_args
        fetch_until "${role_name} signed invoke of default endpoint" "200" "${aspnetcore_url}/health" "identity/resource policy" ${CURL_AUTH[@]+"${CURL_AUTH[@]}"}
        if body_contains '"backend":"aspnetcore"'; then
            record PASS "${role_name} signed invoke body" "backend=aspnetcore"
        else
            record FAIL "${role_name} signed invoke body" "expected backend=aspnetcore"
        fi
    else
        record FAIL "${role_name} signed invoke of default endpoint" "could not assume ${role_arn}"
    fi
done

# 3.4 - unauthorized denied
code="$(fetch "${aspnetcore_url}/health")"
assert_denied "unsigned direct invoke denied" "${code}" 0 "no signature"
if assume "${denied_role_arn}"; then
    sigv4_args
    code="$(fetch "${aspnetcore_url}/health" ${CURL_AUTH[@]+"${CURL_AUTH[@]}"})"
    assert_denied "role without grant denied" "${code}" 1 "signed, no grant"
else
    record FAIL "role without grant denied" "could not assume ${denied_role_arn}"
fi
if assume "${caller_role_arn}"; then
    sigv4_args
    code="$(fetch "${aspnetcore_alt_url}/health" ${CURL_AUTH[@]+"${CURL_AUTH[@]}"})"
    assert_denied "grant is endpoint-scoped" "${code}" 1 "granted role on ungranted endpoint"
else
    record FAIL "grant is endpoint-scoped" "could not assume ${caller_role_arn}"
fi

# 3.6 - live resource policy verification (fail-closed)
nextjs_function_arn="$(web_function_arn_from_endpoint "${nextjs_endpoint_arn}")"
aspnetcore_function_arn="$(web_function_arn_from_endpoint "${aspnetcore_endpoint_arn}")"
blazor_function_arn="$(web_function_arn_from_endpoint "${blazor_endpoint_arn}")"
policy_keys=""
{
    nextjs_policy="$(live_policy_for "${nextjs_function_arn}")"
    aspnetcore_policy="$(live_policy_for "${aspnetcore_function_arn}")"
    blazor_policy="$(live_policy_for "${blazor_function_arn}")"
} 2>/dev/null

nextjs_dist_arns_json="\"${nextjs_dist_arn}\""
[ "${second_distribution}" = "1" ] &&
    nextjs_dist_arns_json="${nextjs_dist_arns_json}, \"${nextjs_second_dist_arn}\""

{
    printf '{"checks": ['
    printf '{"name":"Next.js CloudFront resource policy","key":"nextjs","kind":"cloudfront","endpoint_arn":"%s","dist_arns":[%s]},' \
        "${nextjs_endpoint_arn}" "${nextjs_dist_arns_json}"
    printf '{"name":"Blazor CloudFront resource policy","key":"blazor","kind":"cloudfront","endpoint_arn":"%s","dist_arns":["%s"]},' \
        "${blazor_endpoint_arn}" "${blazor_dist_arn}"
    printf '{"name":"ASP.NET Core resource policy","key":"aspnetcore","kind":"roles","endpoint_arn":"%s","principals":["%s","%s"]}' \
        "${aspnetcore_endpoint_arn}" "${imported_immutable_arn}" "${imported_mutable_arn}"
    printf '], "policies": {"nextjs": %s, "blazor": %s, "aspnetcore": %s}}' \
        "$(python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))' <<<"${nextjs_policy}")" \
        "$(python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))' <<<"${blazor_policy}")" \
        "$(python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))' <<<"${aspnetcore_policy}")"
} > "${SMOKE_TMP}/policy-input.json"

expected_policy_checks=3
policy_output="$(policy_report)"
policy_check_lines="$(printf '%s\n' "${policy_output}" | grep -cE '^(PASS|FAIL|BLOCKED)\|' || true)"
while IFS='|' read -r status name detail; do
    [ -n "${status}" ] || continue
    record "${status}" "${name}" "${detail}"
done <<< "${policy_output}"
if [ "${policy_check_lines}" -eq "${expected_policy_checks}" ]; then
    record PASS "resource policy verification completeness" "${policy_check_lines}/${expected_policy_checks} checks reported"
else
    record FAIL "resource policy verification completeness" "expected ${expected_policy_checks} policy checks, got ${policy_check_lines}"
fi

# CallerRole must carry an identity policy; imported roles must not.
caller_state="$(role_invoke_state "${caller_role_arn}")"
case "${caller_state}" in
    invoke) record PASS "caller role identity policy" "grants lambda:InvokeWebFunctionEndpoint" ;;
    broad) record BLOCKED "caller role identity policy" "caller role has a broad policy; cannot attribute the grant" ;;
    error) record BLOCKED "caller role identity policy" "could not inspect the caller role policies" ;;
    *) record FAIL "caller role identity policy" "expected a scoped invoke grant, state=${caller_state}" ;;
esac
for pair in "imported-immutable:${imported_immutable_arn}" "imported-mutable:${imported_mutable_arn}"; do
    label="${pair%%:*}"
    arn="${pair#*:}"
    state="$(role_invoke_state "${arn}")"
    case "${state}" in
        none) record PASS "${label} role has no identity invoke policy" "resource-policy-only grant" ;;
        invoke) record FAIL "${label} role has no identity invoke policy" "unexpected caller-side invoke grant" ;;
        broad) record BLOCKED "${label} role has no identity invoke policy" "role has a broad policy; effective access ambiguous" ;;
        error) record BLOCKED "${label} role has no identity invoke policy" "could not inspect the role policies" ;;
        *) record BLOCKED "${label} role has no identity invoke policy" "unexpected state ${state}" ;;
    esac
done

# 3.7 - cross-account authorization (optional)
cross_role_arn="${SANDBOX_CROSS_ACCOUNT_CALLER_ROLE_ARN:-}"
if [ -z "${cross_role_arn}" ]; then
    record SKIP "cross-account invocation" "not configured (SANDBOX_CROSS_ACCOUNT_CALLER_ROLE_ARN unset); not verified"
else
    if assume "${cross_role_arn}"; then
        sigv4_args
        cross_code="$(fetch "${aspnetcore_url}/health" ${CURL_AUTH[@]+"${CURL_AUTH[@]}"})"
        if [ "${cross_code}" = "200" ]; then
            record PASS "cross-account authorized invoke" "identity+resource policy -> 200"
        else
            record FAIL "cross-account authorized invoke" "expected 200, got ${cross_code} (negative observed / setup missing)"
        fi
        alt_code="$(fetch "${aspnetcore_alt_url}/health" ${CURL_AUTH[@]+"${CURL_AUTH[@]}"})"
        assert_denied "cross-account ungranted endpoint denied" "${alt_code}" 1 "no grant on alt endpoint"
    else
        record FAIL "cross-account invocation" "required setup missing: could not assume ${cross_role_arn}"
    fi
fi

# 3.8 - workload build id evidence (per workload, from live HTTP)
record PASS "workload build ids" "nextjs=${nextjs_build:-<none>} aspnetcore=${cold_build:-<none>} blazor=${blazor_build:-<none>}"
