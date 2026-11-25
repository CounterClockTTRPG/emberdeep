#!/usr/bin/env bash
# The screenshot walkthrough: runs the real game in a window and writes shots/*.png (a fixed seed, so the same
# build gives the same pictures). Compare with a baseline: shotkit check shots docs/baseline
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
build_if_stale
mkdir -p "$PROJECT_DIR/shots"
# No screen (a server, a terminal over ssh): draw into a virtual one, if there is Xvfb.
WRAP=()
if [ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ] && command -v xvfb-run >/dev/null && [[ "$GODOT" != *.exe ]]; then
    WRAP=(xvfb-run -a -s "-screen 0 1600x900x24")
fi
"${WRAP[@]}" "$GODOT" "${RENDER_ARGS[@]}" --path "$PROJECT_ARG" --resolution 1600x900 -- --shots "$@" 2>&1 | tr -d '\r'
exit "${PIPESTATUS[0]}"
