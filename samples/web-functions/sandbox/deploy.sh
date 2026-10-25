#!/usr/bin/env bash
#
# deploy.sh - deploy the sandbox: support stacks, artifact upload, then the app stack.
#
# Usage:
#   SANDBOX_ALLOW_AWS=1 SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1 \
#   SANDBOX_ID=wfsbx SANDBOX_ACCOUNT=123456789012 SANDBOX_REGION=us-east-1 \
#       ./deploy.sh                      # deploy v1 end to end
#   ... ./deploy.sh --revision 2         # deploy the v2 artifact revision (the v1 -> v2 update)
#   ... ./deploy.sh --second-distribution
#   ... ./deploy.sh --stacks-only        # only the artifacts and external stacks
#   ... ./deploy.sh --revision 2 --skip-upload
#
# The deploy order matters: the artifact objects and the external bucket must exist before the app
# stack creates a revision that points at them.
#
# Every deploy synthesizes the *complete* application (all three stacks). Deploying a reduced app would
# drop the cross-stack exports the app stack imports and break the following deployment.

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

revision=1
second_distribution=0
stacks_only=0
skip_upload=0

while [ "$#" -gt 0 ]; do
    case "$1" in
        --revision | -r)
            [ "$#" -ge 2 ] || die "--revision requires a value (1 or 2)."
            revision="$2"
            shift 2
            ;;
        --second-distribution)
            second_distribution=1
            shift
            ;;
        --stacks-only)
            stacks_only=1
            shift
            ;;
        --skip-upload)
            skip_upload=1
            shift
            ;;
        -h | --help)
            sed -n '2,19p' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        *) die "unknown argument: $1" ;;
    esac
done

[ "${revision}" = "1" ] || [ "${revision}" = "2" ] || die "--revision must be 1 or 2."

validate_sandbox_env
require_aws_allowed
require_shared_account_ack
verify_aws_identity
require_cmd aws
require_cmd dotnet

export JSII_SILENCE_WARNING_UNTESTED_NODE_VERSION=1
# Always synthesize the complete app so support-stack templates keep the cross-stack exports the app
# stack imports.
export SANDBOX_COMPONENTS="all"
# Always set the flag explicitly so disabling the second distribution is never silently ignored.
export SANDBOX_SECOND_DISTRIBUTION="${second_distribution}"

# When reusing an existing upload, validate the manifest completely *before* any AWS mutation, so a
# missing or mismatched manifest can never touch (or replace) the support stacks.
if [ "${skip_upload}" = "1" ]; then
    log "Step 0/3: validate the existing artifact manifest (--skip-upload)."
    manifest_assert_target
    manifest_require_version "v${revision}" nextjs >/dev/null
    manifest_require_version "v${revision}" aspnetcore >/dev/null
    manifest_require_version "v${revision}" blazor >/dev/null
fi

# Step 1: provision the support stacks (buckets, bucket policies, caller roles) before any upload.
log "Step 1/3: deploy artifacts and external stacks."
assert_stack_owned_or_absent "$(stack_name_artifacts)"
assert_stack_owned_or_absent "$(stack_name_external)"

# The support stacks do not depend on artifact versions, but the complete app must still synthesize, so
# use guarded placeholder versions. Only the support stacks are deployed here - the placeholder never
# reaches a deployment.
export SANDBOX_ALLOW_PLACEHOLDER_VERSIONS=1

run_cdk deploy --require-approval never \
    "$(stack_name_artifacts)" "$(stack_name_external)"

if [ "${stacks_only}" = "1" ]; then
    log "Stacks-only deploy complete."
    exit 0
fi

# Step 2: upload artifacts and record their immutable version ids.
if [ "${skip_upload}" = "0" ]; then
    log "Step 2/3: upload artifacts (revision ${revision})."
    "${SANDBOX_DIR}/upload-artifacts.sh" --revision "${revision}"
else
    log "Step 2/3: skipping artifact upload (--skip-upload)."
    [ -f "${MANIFEST_FILE}" ] || die "--skip-upload needs an existing artifact manifest at ${MANIFEST_FILE}."
fi

# Step 3: deploy the app stack with the real, uploaded artifact versions.
log "Step 3/3: deploy app stack with artifact revision v${revision}."
assert_stack_owned_or_absent "$(stack_name_app)"
unset SANDBOX_ALLOW_PLACEHOLDER_VERSIONS
manifest_assert_target
export_artifact_env "v${revision}"

# Guard: never deploy the app stack with a placeholder artifact version.
for version in "${SANDBOX_NEXTJS_OBJECT_VERSION}" "${SANDBOX_ASPNETCORE_OBJECT_VERSION}" "${SANDBOX_BLAZOR_OBJECT_VERSION}"; do
    [ "${version}" != "${SANDBOX_PLACEHOLDER_VERSION}" ] ||
        die "Refusing to deploy the app stack with a placeholder artifact version; upload artifacts first."
done

run_cdk deploy --require-approval never "$(stack_name_app)"

log "Deploy complete. App stack outputs:"
stack_outputs "$(stack_name_app)"
