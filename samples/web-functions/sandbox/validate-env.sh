#!/usr/bin/env bash
#
# validate-env.sh - validate the sandbox target configuration and (optionally) the live AWS identity.
#
# Usage:
#   SANDBOX_ID=wfsbx SANDBOX_ACCOUNT=123456789012 SANDBOX_REGION=us-east-1 ./validate-env.sh
#   SANDBOX_ALLOW_AWS=1 ./validate-env.sh --check-aws
#
# Never calls AWS unless --check-aws (or SANDBOX_ALLOW_AWS=1) is supplied. This script never mutates AWS.
# Mutating scripts additionally require SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1; this script only reports
# whether that acknowledgement is currently set.

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

check_aws=0
[ "${SANDBOX_ALLOW_AWS:-}" = "1" ] && check_aws=1
for arg in "$@"; do
    case "${arg}" in
        --check-aws) check_aws=1 ;;
        -h | --help)
            sed -n '2,12p' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        *) die "unknown argument: ${arg}" ;;
    esac
done

validate_sandbox_env

log "Sandbox target:"
log "  id                 : ${SANDBOX_ID}"
log "  account            : ${SANDBOX_ACCOUNT}"
log "  region             : ${SANDBOX_REGION}"
log "  artifacts stack    : $(stack_name_artifacts)"
log "  external stack     : $(stack_name_external)"
log "  app stack          : $(stack_name_app)"
log "  artifacts bucket   : $(bucket_artifacts)"
log "  external bucket    : $(bucket_external)"
log "  object prefix      : $(object_prefix)"

if [ -d "${ARTIFACT_BUILD_DIR}" ]; then
    log "Local artifact build directory: ${ARTIFACT_BUILD_DIR}"
    for workload in nextjs aspnetcore blazor; do
        if [ -f "${ARTIFACT_BUILD_DIR}/${workload}.zip" ]; then
            log "  found ${workload}.zip"
        else
            warn "missing ${workload}.zip - run ../build-artifacts.sh before uploading"
        fi
    done
else
    warn "local artifact build directory ${ARTIFACT_BUILD_DIR} not found; run ../build-artifacts.sh first"
fi

if [ "${check_aws}" = "1" ]; then
    require_aws_allowed
    verify_aws_identity
else
    log "Skipping AWS identity check (pass --check-aws or set SANDBOX_ALLOW_AWS=1 to enable)."
fi

if [ "${SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT:-}" = "1" ]; then
    log "Shared-account acknowledgement: set (mutating scripts are permitted for this account)."
else
    log "Shared-account acknowledgement: not set (set SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1 before deploy/destroy)."
fi

log "Environment validation OK."