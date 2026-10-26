#!/usr/bin/env bash
#
# synth.sh - synthesize the sandbox CloudFormation templates locally (no AWS calls).
#
# Usage:
#   ./synth.sh                       # uses SANDBOX_* env directly (values may be placeholders)
#   ./synth.sh --version v1          # pulls artifact object versions from build/artifacts.json
#   ./synth.sh --version v2 --second-distribution
#   ./synth.sh --components artifacts,external
#
# This only runs the CDK app; it never deploys. It requires an explicit SANDBOX_ACCOUNT/SANDBOX_REGION.
# When the app stack is synthesized without --version you must either supply the SANDBOX_*_OBJECT_VERSION
# variables or set SANDBOX_ALLOW_PLACEHOLDER_VERSIONS=1 (synthesis only; never deploy a placeholder).

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

artifact_version="${SANDBOX_ARTIFACT_VERSION:-}"
second_distribution=0
components="${SANDBOX_COMPONENTS:-all}"

while [ "$#" -gt 0 ]; do
    case "$1" in
        --version | -v)
            [ "$#" -ge 2 ] || die "--version requires a manifest label (for example v1 or v2)."
            artifact_version="$2"
            shift 2
            ;;
        --second-distribution)
            second_distribution=1
            shift
            ;;
        --components)
            [ "$#" -ge 2 ] || die "--components requires a value (all, or a comma list of artifacts/external/app)."
            components="$2"
            shift 2
            ;;
        -h | --help)
            sed -n '2,14p' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        *) die "unknown argument: $1" ;;
    esac
done

validate_sandbox_env

export SANDBOX_COMPONENTS="${components}"
# Always set explicitly so disabling the second distribution is not silently ignored.
export SANDBOX_SECOND_DISTRIBUTION="${second_distribution}"

if [ -n "${artifact_version}" ]; then
    export_artifact_env "${artifact_version}"
    log "Synthesizing with artifact version label '${artifact_version}'."
elif [[ ",${SANDBOX_COMPONENTS}," == *",app,"* || "${SANDBOX_COMPONENTS}" == "all" ]]; then
    if [ "${SANDBOX_ALLOW_PLACEHOLDER_VERSIONS:-}" = "1" ]; then
        warn "Synthesizing the app stack with placeholder artifact versions (synthesis only)."
    else
        [ -n "${SANDBOX_NEXTJS_OBJECT_VERSION:-}" ] ||
            die "Building the app stack needs SANDBOX_*_OBJECT_VERSION values: pass --version <label> (after upload-artifacts.sh), set them explicitly, or set SANDBOX_ALLOW_PLACEHOLDER_VERSIONS=1 for a synthesis-only run."
    fi
fi

require_cmd dotnet
log "Synthesizing CDK app (components=${SANDBOX_COMPONENTS}) ..."
run_cdk synth --quiet

log "Synthesis complete. Templates written to ${SANDBOX_DIR}/cdk.out:"
