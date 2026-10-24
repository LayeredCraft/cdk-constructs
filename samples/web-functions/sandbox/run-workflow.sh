#!/usr/bin/env bash
#
# run-workflow.sh - convenience sequencer for a full sandbox validation cycle.
#
# Usage:
#   SANDBOX_ALLOW_AWS=1 SANDBOX_ACKNOWLEDGE_NON_PRODUCTION=1 \
#   SANDBOX_ID=wfsbx SANDBOX_ACCOUNT=123456789012 SANDBOX_REGION=us-east-1 \
#       ./run-workflow.sh [--revision N] [--second-distribution] [--skip-smoke]
#
#   ... ./run-workflow.sh --destroy --yes        # validate then destroy (needs SANDBOX_CONFIRM_DESTROY)
#
# deploy.sh synthesizes the complete app itself, so this script does not synthesize up front: a fresh
# checkout can run the workflow without a pre-existing artifact manifest. This script adds no deployment
# logic of its own - it only calls validate-env.sh, deploy.sh, smoke.sh and destroy.sh in order, and each
# of those keeps its own guardrails. Nothing here runs on import or as part of a build; you must invoke it
# explicitly. It never destroys unless --destroy is passed.

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

# Reads the build id baked into the local artifact zip for the given revision, so the smoke checks can
# assert the exact deployed value rather than only its presence. Returns empty (never fails) when the
# artifact zip or unzip is unavailable.
workflow_expected_build_id() {
    local revision="$1" zip
    command -v unzip >/dev/null 2>&1 || return 0
    if [ "${revision}" = "2" ]; then
        zip="${ARTIFACT_BUILD_DIR}/nextjs-v2.zip"
    else
        zip="${ARTIFACT_BUILD_DIR}/nextjs.zip"
    fi
    [ -f "${zip}" ] || return 0
    unzip -p "${zip}" build-id.txt 2>/dev/null | tr -d '\r\n' || true
}

revision=1
second_distribution=0
skip_smoke=0
destroy=0
destroy_args=()

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
        --skip-smoke)
            skip_smoke=1
            shift
            ;;
        --destroy)
            destroy=1
            shift
            ;;
        --yes | -y)
            destroy_args+=(--yes)
            shift
            ;;
        -h | --help)
            sed -n '2,15p' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        *) die "unknown argument: $1" ;;
    esac
done

[ "${revision}" = "1" ] || [ "${revision}" = "2" ] || die "--revision must be 1 or 2."

# Always pass an explicit value so an inherited SANDBOX_SECOND_DISTRIBUTION cannot leak into child scripts.
export SANDBOX_SECOND_DISTRIBUTION="${second_distribution}"

"${SANDBOX_DIR}/validate-env.sh" --check-aws

if [ "${destroy}" = "1" ]; then
    log "Workflow: destroy."
    # shellcheck disable=SC2068
    exec "${SANDBOX_DIR}/destroy.sh" ${destroy_args[@]+"${destroy_args[@]}"}
fi

log "Workflow: 1/2 deploy revision v${revision}."
deploy_args=(--revision "${revision}")
[ "${second_distribution}" = "1" ] && deploy_args+=(--second-distribution)
# shellcheck disable=SC2068
"${SANDBOX_DIR}/deploy.sh" ${deploy_args[@]+"${deploy_args[@]}"}

if [ "${skip_smoke}" = "0" ]; then
    log "Workflow: 2/2 smoke."

    smoke_args=()
    [ "${second_distribution}" = "1" ] && smoke_args+=(--second-distribution)

    # Pass the exact build id baked into the deployed artifact revision, so the smoke checks assert the
    # real value rather than only its presence.
    expected_build_id="$(workflow_expected_build_id "${revision}")"
    [ -n "${expected_build_id}" ] && smoke_args+=(--expect-build-id "${expected_build_id}")

    # If a previous stage recorded per-function revisions, hand them to smoke for the v1 -> v2 comparison.
    revision_state="${SANDBOX_DIR}/build/revision-state.json"
    [ -f "${revision_state}" ] && smoke_args+=(--revision-baseline "${revision_state}")

    # shellcheck disable=SC2068
    "${SANDBOX_DIR}/smoke.sh" ${smoke_args[@]+"${smoke_args[@]}"}

    # Preserve revision/build-id evidence for the next stage (deploy v1, then v2 and compare).
    if [ -f "${SANDBOX_DIR}/build/smoke-summary.json" ]; then
        cp "${SANDBOX_DIR}/build/smoke-summary.json" "${revision_state}"
        log "Recorded revision evidence at ${revision_state}."
    fi
fi

log "Workflow complete."
