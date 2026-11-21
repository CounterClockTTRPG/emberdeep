#!/usr/bin/env bash
# STARTER -- start the game.
#
#   ./play.sh                 play
#   ./play.sh --fullscreen    play full screen (F11 toggles in game too)
#   ./play.sh --editor        open the project in the Godot editor
#   ./play.sh --build         compile the C# assembly and stop
#   ./play.sh --check [--only X]   headless self-check
#   ./play.sh --shots         screenshot walkthrough to shots/*.png
#   ./play.sh --agent         remote-controlled play for an agent (drive it with scripts/agent.sh)
#
# Needs the Godot 4.7 .NET build and a .NET 8+ SDK; paths in .env (see .env.example).
set -uo pipefail
cd "$(dirname "$0")"
case "${1:-}" in
    --editor)  shift; exec bash scripts/editor.sh "$@" ;;
    --build)   shift; exec bash scripts/build.sh "$@" ;;
    --check)   shift; exec bash scripts/check.sh "$@" ;;
    --shots)   shift; exec bash scripts/shots.sh "$@" ;;
    --agent)   shift; exec bash scripts/agent-run.sh "$@" ;;
    --help|-h) sed -n '3,12p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
esac
source scripts/common.sh
require_godot
build_if_stale
ARGS=()
for a in "$@"; do ARGS+=("$a"); done
exec "$GODOT" "${RENDER_ARGS[@]}" --path "$PROJECT_ARG" "${ARGS[@]}"
