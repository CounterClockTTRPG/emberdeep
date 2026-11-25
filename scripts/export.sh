#!/usr/bin/env bash
# Export a Windows build (exe, pack, .NET data, assets beside it), then run the self-check against it.
#   bash scripts/export.sh [WINDOWS_DIR]     (default C:/temp/<Game>)
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
OUT_WIN="${1:-C:/temp/$GAME}"
OUT_WSL="$(wslpath "$OUT_WIN")"
mkdir -p "$OUT_WSL"
rm -rf "${OUT_WSL:?}"/*
"$GODOT" --headless --path "$PROJECT_ARG" --export-release "Windows Desktop" "$OUT_WIN/$GAME.exe" 2>&1 | tr -d '\r' | grep -E "ERROR|DONE" | grep -v "Storing"
cp -r "$PROJECT_DIR/assets" "$OUT_WSL/"
rm -f "$OUT_WSL/assets/.gdignore"
echo "exported to $OUT_WIN; checking the build..."
(cd "$OUT_WSL" && "./$GAME.exe" --headless -- --check 2>&1 | tr -d '\r' | grep -E "FAIL|failed:|checks,")
