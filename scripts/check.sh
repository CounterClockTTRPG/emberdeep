#!/usr/bin/env bash
# The headless self-check (tests/). Exits non-zero if any check fails. --only X runs the sections named like X.
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
build_if_stale
"$GODOT" --headless --path "$PROJECT_ARG" -- --check "$@" 2>&1 | tr -d '\r'
exit "${PIPESTATUS[0]}"
