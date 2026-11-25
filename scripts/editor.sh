#!/usr/bin/env bash
# Open the project in the Godot editor.
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
"$GODOT" "${RENDER_ARGS[@]}" --editor --path "$PROJECT_ARG" "$@" 2>&1 | tr -d '\r'
