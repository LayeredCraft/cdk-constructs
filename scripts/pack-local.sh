#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COUNTER_FILE="$SCRIPT_DIR/.counter"
OUTPUT_DIR="/usr/local/share/nuget/local"

# Read VersionPrefix from Directory.Build.props
VERSION_PREFIX=$(sed -n 's/.*<VersionPrefix>\(.*\)<\/VersionPrefix>.*/\1/p' "$REPO_ROOT/Directory.Build.props" | tr -d '[:space:]')

if [[ -z "$VERSION_PREFIX" ]]; then
  echo "Error: Could not read VersionPrefix from Directory.Build.props" >&2
  exit 1
fi

# Read or initialize the counter
if [[ -f "$COUNTER_FILE" ]]; then
  COUNTER=$(cat "$COUNTER_FILE")
else
  COUNTER=1
fi

VERSION="${VERSION_PREFIX}-local.${COUNTER}"

echo "Packing version: $VERSION"
echo "Output directory: $OUTPUT_DIR"

mkdir -p "$OUTPUT_DIR"

dotnet pack "$REPO_ROOT/LayeredCraft.Cdk.Constructs.slnx" \
  /p:Version="$VERSION" \
  --configuration Release \
  --output "$OUTPUT_DIR" \
  --no-restore

echo ""
echo "Packed successfully: $VERSION"
echo "Packages written to: $OUTPUT_DIR"

# Increment and persist the counter
COUNTER=$((COUNTER + 1))
echo "$COUNTER" > "$COUNTER_FILE"