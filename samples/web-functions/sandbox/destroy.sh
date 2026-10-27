#!/usr/bin/env bash
#
# destroy.sh - tear down the sandbox and every resource it owns. Destructive.
#
# Usage:
#   SANDBOX_ALLOW_AWS=1 SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1 SANDBOX_CONFIRM_DESTROY=wfsbx \
#   SANDBOX_ID=wfsbx SANDBOX_ACCOUNT=123456789012 SANDBOX_REGION=us-east-1 \
#       ./destroy.sh --yes
#
# Gates (all required) so this cannot fire accidentally or against the wrong target:
#   1. --yes on the command line
#   2. SANDBOX_CONFIRM_DESTROY equal to SANDBOX_ID
#   3. SANDBOX_ALLOW_AWS=1 and SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1
# The live AWS account and Region are verified before anything is deleted, and each stack is proven to
# be sandbox-owned (ownership tag + SandboxId output) before it is touched. The sandbox-owned buckets
# are emptied (all versions and delete markers) and then deleted by CloudFormation, never directly.
# CloudFormation-owned log groups are likewise deleted with their stacks. The single exception is the
# companion ".local" group Lambda creates on first use for a Use-mode function (it is never owned by
# CloudFormation, so it survives stack deletion): that group is removed by its exact, sandbox-namespaced
# name, and only after confirming it exists. No resource is ever deleted by wildcard or by guessing.

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

confirmed=0
while [ "$#" -gt 0 ]; do
    case "$1" in
        --yes | -y)
            confirmed=1
            shift
            ;;
        -h | --help)
            sed -n '2,17p' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        *) die "unknown argument: $1" ;;
    esac
done

validate_sandbox_env
require_aws_allowed
require_shared_account_ack

# Destructive-intent gates run before any AWS call, so an unconfirmed invocation never even reaches STS.
[ "${confirmed}" = "1" ] || die "Refusing to destroy without --yes."
[ "${SANDBOX_CONFIRM_DESTROY:-}" = "${SANDBOX_ID}" ] ||
    die "Set SANDBOX_CONFIRM_DESTROY=${SANDBOX_ID} to confirm you intend to destroy sandbox '${SANDBOX_ID}'."

verify_aws_identity
require_cmd aws
require_cmd dotnet

export JSII_SILENCE_WARNING_UNTESTED_NODE_VERSION=1
export SANDBOX_COMPONENTS="all"
export SANDBOX_SECOND_DISTRIBUTION="${SANDBOX_SECOND_DISTRIBUTION:-0}"
# Teardown only synthesizes, so placeholder versions are safe and keep destroy working even when the
# manifest was lost or a previous deployment failed partway through.
export SANDBOX_ALLOW_PLACEHOLDER_VERSIONS=1

# Destroys an owned stack and confirms it is actually gone. A foreign or ambiguous stack aborts.
destroy_owned_stack() {
    local stack="$1" status
    status="$(stack_ownership "${stack}")"
    case "${status}" in
        owned)
            log "Destroying stack ${stack}."
            run_cdk destroy "${stack}" --force
            ;;
        absent)
            warn "Stack ${stack} not present; skipping."
            return 0
            ;;
        foreign:*)
            die "Stack ${stack} exists but is not owned by sandbox '${SANDBOX_ID}' (${status#foreign:}). Refusing to delete it."
            ;;
        error:*)
            die "Could not determine ownership of stack ${stack} (${status#error:}). Refusing to continue (fail closed)."
            ;;
        *)
            die "Unexpected ownership status '${status}' for stack ${stack}."
            ;;
    esac

    # Never report success while a targeted stack remains.
    status="$(stack_ownership "${stack}")"
    case "${status}" in
        absent) log "Confirmed stack ${stack} is gone." ;;
        owned | foreign:*)
            die "Stack ${stack} still exists after destroy (${status}). Not removing the manifest."
            ;;
        error:*)
            die "Could not confirm removal of stack ${stack} (${status#error:}). Not removing the manifest."
            ;;
        *)
            die "Unexpected ownership status '${status}' for stack ${stack}."
            ;;
    esac
}

log "Destroying sandbox '${SANDBOX_ID}' in account ${SANDBOX_ACCOUNT} / ${SANDBOX_REGION}."

# Order: app (functions/revisions reference the buckets) then external then artifacts.
destroy_owned_stack "$(stack_name_app)"

# Empty the sandbox-owned buckets before CloudFormation deletes them; never delete a bucket directly.
for stack_bucket in "$(stack_name_external):$(bucket_external)" "$(stack_name_artifacts):$(bucket_artifacts)"; do
    stack="${stack_bucket%%:*}"
    bucket="${stack_bucket#*:}"
    status="$(stack_ownership "${stack}")"
    if [ "${status}" = "owned" ] && bucket_exists "${bucket}"; then
        log "Emptying ${bucket} (all object versions and delete markers)."
        empty_bucket "${bucket}"
    fi
    destroy_owned_stack "${stack}"
done

# CloudWatch log groups owned by CloudFormation are deleted with their stacks. The one group Lambda
# creates on first use (the ".local" companion of a Use-mode function, which CloudFormation never owns)
# is not covered by stack deletion, so remove it explicitly by exact name.
delete_sandbox_log_group() {
    local name="$1" required_prefix="/${SANDBOX_NAMESPACE}/${SANDBOX_ID}-"
    case "${name}" in
        "${required_prefix}"*)
            ;;
        *)
            die "Refusing to delete log group '${name}': it is not within the sandbox namespace '${required_prefix}'."
            ;;
    esac

    local listed
    listed="$(aws_cmd logs describe-log-groups --log-group-name-prefix "${name}" --output json 2>/dev/null || printf '{"logGroups":[]}')"
    local present
    present="$(printf '%s' "${listed}" | python3 -c 'import json,sys
name=sys.argv[1]
groups=json.load(sys.stdin).get("logGroups",[])
print("yes" if any(g.get("logGroupName")==name for g in groups) else "no")' "${name}")"

    if [ "${present}" != "yes" ]; then
        warn "Log group ${name} not present; nothing to remove."
        return 0
    fi

    log "Removing service-created log group ${name} (not owned by CloudFormation)."
    aws_cmd logs delete-log-group --log-group-name "${name}" >/dev/null

    listed="$(aws_cmd logs describe-log-groups --log-group-name-prefix "${name}" --output json 2>/dev/null || printf '{"logGroups":[]}')"
    present="$(printf '%s' "${listed}" | python3 -c 'import json,sys
name=sys.argv[1]
groups=json.load(sys.stdin).get("logGroups",[])
print("yes" if any(g.get("logGroupName")==name for g in groups) else "no")' "${name}")"
    if [ "${present}" = "yes" ]; then
        die "Log group ${name} still exists after deletion. Not removing the manifest."
    fi
    log "Confirmed log group ${name} is gone."
}

# Strictly scoped to the sandbox namespace; only the known service-created companion group is targeted.
delete_sandbox_log_group "$(uselogs_companion_group)"

# Only remove the manifest once every stack has been confirmed gone.
rm -f "${MANIFEST_FILE}"

log "Sandbox '${SANDBOX_ID}' destroyed."
