#!/usr/bin/env bash
# Compile the C# assembly and stop.
set -uo pipefail
source "$(dirname "$0")/common.sh"
rm -f "$ASSEMBLY" 2>/dev/null
build_if_stale
echo "built: $ASSEMBLY"
