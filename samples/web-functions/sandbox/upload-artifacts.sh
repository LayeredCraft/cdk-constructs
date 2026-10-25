#!/usr/bin/env bash
#
# upload-artifacts.sh - upload a built artifact revision to the versioned sandbox buckets and record
# the immutable object version ids in the local manifest.
#
# Usage:
#   SANDBOX_ALLOW_AWS=1 SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1 \
#   SANDBOX_ID=wfsbx SANDBOX_ACCOUNT=123456789012 SANDBOX_REGION=us-east-1 \
#       ./upload-artifacts.sh --revision 1
#   ... ./upload-artifacts.sh --revision 2      # uploads the -v2.zip build outputs
#
# Requires the artifacts and external stacks to already exist (run deploy.sh first, or --stacks-only).
# Never rebuilds artifacts; it consumes the ZIPs produced by ../build-artifacts.sh.

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

revision=1
while [ "$#" -gt 0 ]; do
    case "$1" in
        --revision | -r)
            [ "$#" -ge 2 ] || die "--revision requires a value (1 or 2)."
            revision="$2"
            shift 2
            ;;
        -h | --help)
            sed -n '2,16p' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        *) die "unknown argument: $1" ;;
    esac
done

[ "${revision}" = "1" ] || [ "${revision}" = "2" ] || die "--revision must be 1 or 2."
label="v${revision}"

validate_sandbox_env
require_aws_allowed
require_shared_account_ack
verify_aws_identity
require_cmd aws

artifacts_bucket="$(bucket_artifacts)"
external_bucket="$(bucket_external)"
prefix="$(object_prefix)"

# Refuse to merge into a manifest that belongs to a different sandbox target.
if [ -f "${MANIFEST_FILE}" ]; then
    manifest_assert_target
fi

bucket_exists "${artifacts_bucket}" ||
    die "artifacts bucket ${artifacts_bucket} not found; deploy the artifacts stack first (deploy.sh --stacks-only)."
bucket_exists "${external_bucket}" ||
    die "external bucket ${external_bucket} not found; deploy the external stack first (deploy.sh --stacks-only)."

upload_one() {
    local workload="$1" bucket="$2" source_file="$3" key="$4"
    [ -f "${source_file}" ] || die "build output ${source_file} not found; run ../build-artifacts.sh first."
    local version_id
    version_id="$(aws_cmd s3api put-object \
        --bucket "${bucket}" \
        --expected-bucket-owner "${SANDBOX_ACCOUNT}" \
        --key "${key}" \
        --body "${source_file}" \
        --query VersionId --output text)" || die "failed to upload ${source_file} to s3://${bucket}/${key}."
    [ -n "${version_id}" ] && [ "${version_id}" != "None" ] ||
        die "s3://${bucket}/${key} did not return a VersionId; is bucket versioning enabled?"
    log "Uploaded ${workload}: s3://${bucket}/${key} (version ${version_id})"
    printf '%s' "${version_id}"
}

if [ "${revision}" = "2" ]; then
    nextjs_src="${ARTIFACT_BUILD_DIR}/nextjs-v2.zip"
    aspnetcore_src="${ARTIFACT_BUILD_DIR}/aspnetcore-v2.zip"
    blazor_src="${ARTIFACT_BUILD_DIR}/blazor-v2.zip"
else
    nextjs_src="${ARTIFACT_BUILD_DIR}/nextjs.zip"
    aspnetcore_src="${ARTIFACT_BUILD_DIR}/aspnetcore.zip"
    blazor_src="${ARTIFACT_BUILD_DIR}/blazor.zip"
fi

nextjs_version="$(upload_one nextjs "${artifacts_bucket}" "${nextjs_src}" "${prefix}/nextjs.zip")"
aspnetcore_version="$(upload_one aspnetcore "${external_bucket}" "${aspnetcore_src}" "${prefix}/aspnetcore.zip")"
blazor_version="$(upload_one blazor "${external_bucket}" "${blazor_src}" "${prefix}/blazor.zip")"

mkdir -p "$(dirname "${MANIFEST_FILE}")"
python3 - "${MANIFEST_FILE}" "${label}" "${SANDBOX_ID}" "${SANDBOX_ACCOUNT}" "${SANDBOX_REGION}" \
    "${prefix}" "${artifacts_bucket}" "${external_bucket}" \
    "${nextjs_version}" "${aspnetcore_version}" "${blazor_version}" <<'PY'
import json, os, sys
(path, label, sandbox_id, account, region, prefix,
 artifacts_bucket, external_bucket, nextjs, aspnetcore, blazor) = sys.argv[1:]

data = {}
if os.path.exists(path):
    with open(path) as fh:
        data = json.load(fh)

data.update({
    "sandboxId": sandbox_id,
    "account": account,
    "region": region,
    "prefix": prefix,
    "artifactsBucket": artifacts_bucket,
    "externalBucket": external_bucket,
})
data.setdefault("versions", {})[label] = {
    "nextjs": nextjs,
    "aspnetcore": aspnetcore,
    "blazor": blazor,
}

with open(path, "w") as fh:
    json.dump(data, fh, indent=2, sort_keys=True)
    fh.write("\n")
PY

log "Recorded artifact revisions '${label}' in ${MANIFEST_FILE}."
