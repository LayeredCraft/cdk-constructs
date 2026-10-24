#!/usr/bin/env bash
#
# update-to-v2.sh - deploy the second artifact revision, exercising the v1 -> v2 revision update.
#
# Usage:
#   SANDBOX_ALLOW_AWS=1 SANDBOX_ID=wfsbx SANDBOX_ACCOUNT=123456789012 SANDBOX_REGION=us-east-1 \
#       ./update-to-v2.sh
#
# This is a small wrapper over deploy.sh --revision 2: it uploads the -v2.zip artifacts and deploys the
# app stack with the new object versions, so existing endpoints move to the new revision without being
# replaced. Run it only after a successful v1 deploy.

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

exec "${SANDBOX_DIR}/deploy.sh" --revision 2 "$@"