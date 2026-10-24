#!/usr/bin/env bash
#
# Shared configuration, naming, and safety guardrails for the Web Function sandbox tooling.
#
# This file is *sourced* by the other scripts; it is never executed directly and it never performs an
# AWS-mutating action by itself.
#
# Guardrail model:
#   * The sandbox target (account, Region, id) is always supplied explicitly - nothing is inferred, and
#     an inherited AWS profile or AWS_* environment variable can never silently retarget a command
#     (every AWS call is pinned with --region, and a disagreeing ambient region aborts).
#   * Every AWS-mutating script must be opted in with SANDBOX_ALLOW_AWS=1 *and* acknowledge a
#     non-production account with SANDBOX_ACKNOWLEDGE_NON_PRODUCTION=1, then verifies the live AWS
#     identity (STS account and Region) against the declared sandbox target before doing anything.
#   * Every stack name carries a fixed sandbox namespace and every stack carries an ownership tag, so a
#     pre-existing stack is proven to belong to this sandbox before it is updated or deleted.

set -o errexit
set -o nounset
set -o pipefail

SANDBOX_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SAMPLES_DIR="$(cd "${SANDBOX_DIR}/.." && pwd)"
ARTIFACT_BUILD_DIR="${SAMPLES_DIR}/build"
MANIFEST_FILE="${SANDBOX_DIR}/build/artifacts.json"
PROJECT_FILE="${SANDBOX_DIR}/WebFunctionSandbox.csproj"

# Fixed, distinctive namespace and ownership markers. These MUST match SandboxNaming.cs.
SANDBOX_NAMESPACE="lccdk-wf-sandbox"
SANDBOX_OWNER="LayeredCraft.Cdk.Constructs"
SANDBOX_OWNERSHIP_TAG="lc-webfunction-sandbox-id"
SANDBOX_OWNER_TAG="lc-webfunction-sandbox-owner"

# Single pinned CDK CLI version. Amazon.CDK.Lib 2.274.0 emits cloud assembly schema 55, which needs CLI
# 2.1145.0 or newer. Override with SANDBOX_CDK (for example SANDBOX_CDK="npx --yes aws-cdk@2.1146.1").
CDK_CLI_VERSION="2.1145.0"

# Sentinel used only for synthesis when artifact versions are not yet known (must match
# ArtifactReference.PlaceholderVersion in SandboxConfiguration.cs). It must never be deployed.
SANDBOX_PLACEHOLDER_VERSION="__SANDBOX_PLACEHOLDER__"

log() { printf '[sandbox] %s\n' "$*" >&2; }
warn() { printf '[sandbox] WARNING: %s\n' "$*" >&2; }
err() { printf '[sandbox] ERROR: %s\n' "$*" >&2; }
die() {
    err "$*"
    exit 1
}

require_cmd() {
    command -v "$1" >/dev/null 2>&1 || die "required command not found on PATH: $1"
}

# Validates the explicit sandbox target. Fails early and lists the offending value.
validate_sandbox_env() {
    [ -n "${SANDBOX_ID:-}" ] || die "SANDBOX_ID is required (a short lowercase namespace for this sandbox)."
    [ -n "${SANDBOX_ACCOUNT:-}" ] || die "SANDBOX_ACCOUNT is required (the 12-digit disposable sandbox account id)."
    [ -n "${SANDBOX_REGION:-}" ] || die "SANDBOX_REGION is required (for example us-east-1)."

    [[ "${SANDBOX_ACCOUNT}" =~ ^[0-9]{12}$ ]] || die "SANDBOX_ACCOUNT must be a 12-digit AWS account id."
    [[ "${SANDBOX_REGION}" =~ ^[a-z]{2}-[a-z]+-[0-9]$ ]] || die "SANDBOX_REGION must look like us-east-1."
    [[ "${SANDBOX_ID}" =~ ^[a-z][a-z0-9-]{1,14}[a-z0-9]$ ]] ||
        die "SANDBOX_ID must be 3-16 lowercase alphanumeric/hyphen characters and start and end with an alphanumeric character."

    if [ -n "${SANDBOX_ARTIFACTS_PREFIX:-}" ]; then
        [[ "${SANDBOX_ARTIFACTS_PREFIX}" =~ ^[a-z0-9][a-z0-9/_-]{0,62}$ ]] ||
            die "SANDBOX_ARTIFACTS_PREFIX must be a short lowercase S3 key prefix (letters, digits, '/', '_', '-')."
    fi

    export SANDBOX_ID SANDBOX_ACCOUNT SANDBOX_REGION
}

# Opt-in gate for anything that talks to the AWS control/data plane.
require_aws_allowed() {
    [ "${SANDBOX_ALLOW_AWS:-}" = "1" ] ||
        die "Refusing to call AWS. Set SANDBOX_ALLOW_AWS=1 to acknowledge you are acting on the disposable sandbox account ${SANDBOX_ACCOUNT:-<unset>}."
}

# Explicit acknowledgement that the target account is disposable and non-production.
require_non_production_ack() {
    [ "${SANDBOX_ACKNOWLEDGE_NON_PRODUCTION:-}" = "1" ] ||
        die "Refusing to mutate AWS. Set SANDBOX_ACKNOWLEDGE_NON_PRODUCTION=1 to confirm SANDBOX_ACCOUNT ${SANDBOX_ACCOUNT:-<unset>} is a disposable non-production account."
}

# Every AWS call goes through this wrapper so an inherited profile/region cannot retarget it.
aws_cmd() {
    aws --region "${SANDBOX_REGION}" "$@"
}

# Verifies the live AWS identity matches the declared sandbox before any mutation.
verify_aws_identity() {
    require_cmd aws

    local live_account live_region
    live_account="$(aws_cmd sts get-caller-identity --query Account --output text 2>/dev/null)" ||
        die "aws sts get-caller-identity failed; check your credentials and profile."

    [ "${live_account}" = "${SANDBOX_ACCOUNT}" ] ||
        die "Live AWS account ${live_account} does not match SANDBOX_ACCOUNT ${SANDBOX_ACCOUNT}. Refusing to continue."

    # aws_cmd pins --region, but also refuse to proceed when an inherited region disagrees, so the ambient
    # environment cannot silently point the deployment somewhere other than SANDBOX_REGION.
    live_region="${AWS_REGION:-${AWS_DEFAULT_REGION:-$(aws configure get region 2>/dev/null || true)}}"
    if [ -n "${live_region}" ] && [ "${live_region}" != "${SANDBOX_REGION}" ]; then
        die "Ambient AWS region '${live_region}' does not match SANDBOX_REGION '${SANDBOX_REGION}'. Unset AWS_REGION/AWS_DEFAULT_REGION or fix the profile, then retry."
    fi

    log "Verified sandbox identity: account=${live_account} region=${SANDBOX_REGION}"
}

# --- Deterministic names (must match SandboxNaming.cs) --------------------------------------------

stack_name_artifacts() { printf '%s-%s-artifacts' "${SANDBOX_NAMESPACE}" "${SANDBOX_ID}"; }
stack_name_external() { printf '%s-%s-external' "${SANDBOX_NAMESPACE}" "${SANDBOX_ID}"; }
stack_name_app() { printf '%s-%s-app' "${SANDBOX_NAMESPACE}" "${SANDBOX_ID}"; }

bucket_artifacts() { printf '%s-artifacts-%s-%s' "${SANDBOX_ID}" "${SANDBOX_ACCOUNT}" "${SANDBOX_REGION}"; }
bucket_external() { printf '%s-external-%s-%s' "${SANDBOX_ID}" "${SANDBOX_ACCOUNT}" "${SANDBOX_REGION}"; }

object_prefix() { printf '%s' "${SANDBOX_ARTIFACTS_PREFIX:-${SANDBOX_ID}}"; }

nextjs_function() { printf '%s-nextjs' "${SANDBOX_ID}"; }
aspnetcore_function() { printf '%s-aspnetcore' "${SANDBOX_ID}"; }
blazor_function() { printf '%s-blazor' "${SANDBOX_ID}"; }

# --- Stack ownership ------------------------------------------------------------------------------
#
# Returns one of: absent | owned | foreign:<reason> | error:<message>. Never fails closed silently:
# an ambiguous describe result (throttling, expired credentials, AccessDenied) is reported as error so
# callers can abort rather than assume the stack is gone.

stack_ownership() {
    local stack="$1" raw rc

    set +e
    raw="$(aws_cmd cloudformation describe-stacks --stack-name "${stack}" \
        --query 'Stacks[0].Tags' --output json 2>&1)"
    rc=$?
    set -e

    if [ "${rc}" -ne 0 ]; then
        case "${raw}" in
            *"does not exist"*) printf 'absent' ;;
            *) printf 'error:%s' "$(printf '%s' "${raw}" | tr '\n' ' ' | cut -c1-200)" ;;
        esac
        return 0
    fi

    local id owner
    id="$(_tag_value "${raw}" "${SANDBOX_OWNERSHIP_TAG}")"
    owner="$(_tag_value "${raw}" "${SANDBOX_OWNER_TAG}")"

    if [ "${owner}" != "${SANDBOX_OWNER}" ]; then
        printf 'foreign:owner-tag=%s' "${owner:-<none>}"
        return 0
    fi

    if [ "${id}" != "${SANDBOX_ID}" ]; then
        printf 'foreign:sandbox-id-tag=%s' "${id:-<none>}"
        return 0
    fi

    # When the stack exposes a SandboxId output, it must agree as well.
    local out_id
    out_id="$(stack_output "${stack}" SandboxId 2>/dev/null || true)"
    if [ -n "${out_id}" ] && [ "${out_id}" != "${SANDBOX_ID}" ]; then
        printf 'foreign:SandboxId-output=%s' "${out_id}"
        return 0
    fi

    printf 'owned'
}

_tag_value() {
    python3 -c 'import json,sys
tags = json.loads(sys.argv[1] or "[]") or []
d = {t.get("Key"): t.get("Value") for t in tags}
sys.stdout.write(d.get(sys.argv[2]) or "")' "$1" "$2"
}

# For deploys: a stack may be absent (first deploy) or owned; anything else aborts (fail closed).
assert_stack_owned_or_absent() {
    local stack="$1" status
    status="$(stack_ownership "${stack}")"
    case "${status}" in
        absent) log "Stack ${stack} not present yet (first deploy)." ;;
        owned) log "Stack ${stack} verified as sandbox-owned." ;;
        foreign:*) die "Stack ${stack} already exists but is not owned by sandbox '${SANDBOX_ID}' (${status#foreign:}). Refusing to modify it." ;;
        error:*) die "Could not determine ownership of stack ${stack} (${status#error:}). Refusing to continue (fail closed)." ;;
        *) die "Unexpected ownership status '${status}' for stack ${stack}." ;;
    esac
}

# For destroys: only an owned stack may be deleted; a foreign/ambiguous stack aborts (fail closed).
require_stack_owned() {
    local stack="$1" status
    status="$(stack_ownership "${stack}")"
    case "${status}" in
        owned) log "Stack ${stack} verified as sandbox-owned." ;;
        absent) die "Stack ${stack} does not exist; refusing to guess." ;;
        foreign:*) die "Stack ${stack} exists but is not owned by sandbox '${SANDBOX_ID}' (${status#foreign:}). Refusing to delete it." ;;
        error:*) die "Could not determine ownership of stack ${stack} (${status#error:}). Refusing to continue (fail closed)." ;;
        *) die "Unexpected ownership status '${status}' for stack ${stack}." ;;
    esac
}

# --- CDK CLI resolution ---------------------------------------------------------------------------

run_cdk() {
    if [ -n "${SANDBOX_CDK:-}" ]; then
        # shellcheck disable=SC2086
        ${SANDBOX_CDK} "$@"
    else
        require_cmd npx
        npx --yes "aws-cdk@${CDK_CLI_VERSION}" "$@"
    fi
}

# --- Artifact manifest helpers --------------------------------------------------------------------
#
# The manifest (build/artifacts.json, ignored by git) records the bucket/key/version identity of each
# uploaded artifact version so deployments are reproducible without rebuilding or re-uploading.

manifest_field() {
    [ -f "${MANIFEST_FILE}" ] ||
        die "artifact manifest ${MANIFEST_FILE} not found; run upload-artifacts.sh first."
    python3 -c 'import json,sys
d = json.load(open(sys.argv[1]))
for key in sys.argv[2].split("."):
    d = d[key]
sys.stdout.write(str(d))' "${MANIFEST_FILE}" "$1"
}

# Confirms a manifest belongs to the currently configured sandbox before it is reused.
manifest_assert_target() {
    [ -f "${MANIFEST_FILE}" ] ||
        die "artifact manifest ${MANIFEST_FILE} not found; run upload-artifacts.sh first."

    local m_id m_acct m_region m_prefix
    m_id="$(manifest_field sandboxId)"
    m_acct="$(manifest_field account)"
    m_region="$(manifest_field region)"
    m_prefix="$(manifest_field prefix)"

    [ "${m_id}" = "${SANDBOX_ID}" ] ||
        die "Manifest sandboxId '${m_id}' does not match SANDBOX_ID '${SANDBOX_ID}'. Refusing to reuse a foreign manifest."
    [ "${m_acct}" = "${SANDBOX_ACCOUNT}" ] ||
        die "Manifest account '${m_acct}' does not match SANDBOX_ACCOUNT '${SANDBOX_ACCOUNT}'."
    [ "${m_region}" = "${SANDBOX_REGION}" ] ||
        die "Manifest region '${m_region}' does not match SANDBOX_REGION '${SANDBOX_REGION}'."
    [ "${m_prefix}" = "$(object_prefix)" ] ||
        die "Manifest prefix '${m_prefix}' does not match the configured artifact prefix '$(object_prefix)'."
}

manifest_require_version() {
    local label="$1" workload="$2" value
    value="$(manifest_field "versions.${label}.${workload}" 2>/dev/null)" ||
        die "manifest has no version for label '${label}' workload '${workload}'. Upload it first."
    [ -n "${value}" ] || die "manifest version for '${label}/${workload}' is empty."
    printf '%s' "${value}"
}

# Exports the SANDBOX_* variables the CDK app reads, for one deployed artifact version label.
#
# Each value is assigned on its own line before being exported: `export X="$(cmd)"` would mask a failing
# command substitution behind export's zero exit status, defeating errexit.
export_artifact_env() {
    local label="$1" key version

    key="$(object_prefix)"

    version="$(manifest_require_version "${label}" nextjs)"
    export SANDBOX_NEXTJS_OBJECT_KEY="${key}/nextjs.zip"
    export SANDBOX_NEXTJS_OBJECT_VERSION="${version}"

    version="$(manifest_require_version "${label}" aspnetcore)"
    export SANDBOX_ASPNETCORE_OBJECT_KEY="${key}/aspnetcore.zip"
    export SANDBOX_ASPNETCORE_OBJECT_VERSION="${version}"

    version="$(manifest_require_version "${label}" blazor)"
    export SANDBOX_BLAZOR_OBJECT_KEY="${key}/blazor.zip"
    export SANDBOX_BLAZOR_OBJECT_VERSION="${version}"
}

stack_outputs() {
    local stack="$1"
    aws_cmd cloudformation describe-stacks --stack-name "${stack}" \
        --query 'Stacks[0].Outputs' --output json
}

stack_output() {
    local stack="$1" key="$2"
    stack_outputs "${stack}" |
        python3 -c 'import json,sys
outputs = json.load(sys.stdin) or []
for o in outputs:
    if o["OutputKey"] == sys.argv[1]:
        sys.stdout.write(o["OutputValue"]); break' "$key"
}

# True only when the bucket exists *and* is owned by the sandbox account.
bucket_exists() {
    local bucket="$1" rc
    set +e
    aws_cmd s3api head-bucket --bucket "${bucket}" --expected-bucket-owner "${SANDBOX_ACCOUNT}" >/dev/null 2>&1
    rc=$?
    set -e
    [ "${rc}" -eq 0 ]
}

# Deletes every object version and delete marker from a versioned bucket, but never the bucket itself:
# the bucket is owned by CloudFormation and must be deleted by the stack that created it.
empty_bucket() {
    local bucket="$1"
    python3 - "${bucket}" "${SANDBOX_ACCOUNT}" "${SANDBOX_REGION}" <<'PY'
import json, subprocess, sys

bucket, owner, region = sys.argv[1], sys.argv[2], sys.argv[3]
base = ["aws", "s3api", "--region", region]

def run(args):
    result = subprocess.run(args, capture_output=True, text=True)
    if result.returncode != 0:
        sys.stderr.write(result.stderr)
        sys.exit(result.returncode)
    return result.stdout

while True:
    data = json.loads(run(base + [
        "list-object-versions", "--bucket", bucket,
        "--expected-bucket-owner", owner, "--max-keys", "1000", "--output", "json",
    ]) or "{}")
    objects = [{"Key": v["Key"], "VersionId": v["VersionId"]} for v in (data.get("Versions") or [])]
    objects += [{"Key": m["Key"], "VersionId": m["VersionId"]} for m in (data.get("DeleteMarkers") or [])]
    if not objects:
        break
    for i in range(0, len(objects), 1000):
        batch = {"Objects": objects[i:i + 1000], "Quiet": True}
        run(base + [
            "delete-objects", "--bucket", bucket,
            "--expected-bucket-owner", owner, "--delete", json.dumps(batch),
        ])
    if len(objects) < 1000:
        break

print("emptied")
PY
}
