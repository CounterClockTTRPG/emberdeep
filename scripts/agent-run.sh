#!/usr/bin/env bash
# Run the game windowed under remote control (godot-agent): it reads agent/cmd.txt and answers in agent/out.txt.
# Drive it with scripts/agent.sh. It plays in its own save slot, never the player's.
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
build_if_stale
mkdir -p "$PROJECT_DIR/agent"
# No screen (a server, a terminal over ssh): play in a virtual one, if there is Xvfb.
WRAP=()
if [ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ] && command -v xvfb-run >/dev/null && [[ "$GODOT" != *.exe ]]; then
    WRAP=(xvfb-run -a -s "-screen 0 1600x900x24")
fi
"${WRAP[@]}" "$GODOT" "${RENDER_ARGS[@]}" --path "$PROJECT_ARG" --resolution 1600x900 -- --agent "$@" 2>&1 | tr -d '\r'
exit "${PIPESTATUS[0]}"
