#!/usr/bin/env bash
# The screenshot walkthrough: runs the real game in a window and writes shots/*.png (a fixed seed, so the same
# build gives the same pictures). Compare with a baseline: shotkit check shots docs/baseline
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
build_if_stale
mkdir -p "$PROJECT_DIR/shots"
"$GODOT" "${RENDER_ARGS[@]}" --path "$PROJECT_ARG" --resolution 1600x900 -- --shots "$@" 2>&1 | tr -d '\r'
exit "${PIPESTATUS[0]}"
