#!/usr/bin/env bash
# Shared setup for every tool script. The game's name comes from project.godot (dotnet/project/assembly_name).
#
# On this kind of machine Godot and dotnet are Windows executables driven from WSL: the project is handed to them
# as a UNC path into the WSL filesystem, and their output arrives with CRLF line endings that callers strip.

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [ -f "$PROJECT_DIR/.env" ]; then set -a; source "$PROJECT_DIR/.env"; set +a; fi

GAME="$(sed -n 's/^project\/assembly_name="\(.*\)"/\1/p' "$PROJECT_DIR/project.godot")"
GODOT_DIR="${GODOT_DIR:-/mnt/c/data/tools/Godot_v4.7.2-stable_mono_win64}"
GODOT="${GODOT:-$GODOT_DIR/Godot_v4.7.2-stable_mono_win64_console.exe}"
PROJECT_UNC="\\\\wsl.localhost\\${WSL_DISTRO_NAME:-Ubuntu}$(printf '%s' "$PROJECT_DIR" | tr '/' '\\')"
ASSEMBLY="$PROJECT_DIR/.godot/mono/temp/bin/Debug/$GAME.dll"
# Forward+ can crash weak Vulkan drivers at start-up (SDFGI debug shaders); mobile is still Vulkan and safe.
RENDER_METHOD="${RENDER_METHOD:-mobile}"
RENDER_ARGS=(--rendering-method "$RENDER_METHOD")

# The Godot binary: on Windows, give Godot the UNC path; a Linux Godot takes the plain one.
case "$GODOT" in *.exe) PROJECT_ARG="$PROJECT_UNC" ;; *) PROJECT_ARG="$PROJECT_DIR" ;; esac

require_godot() {
    [ -x "$GODOT" ] && return 0
    echo "Godot (4.7 .NET build) not found at: $GODOT -- set GODOT in .env (see .env.example)" >&2
    exit 127
}

# Building with dotnet directly: seconds, and a real exit code (Godot's --build-solutions is slow and may not exit).
DOTNET="${DOTNET:-}"
if [ -z "$DOTNET" ]; then
    if command -v dotnet >/dev/null 2>&1; then DOTNET="$(command -v dotnet)"
    elif [ -x "/mnt/c/Program Files/dotnet/dotnet.exe" ]; then DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"; fi
fi
case "$DOTNET" in *.exe) CSPROJ_ARG="$PROJECT_UNC\\$GAME.csproj" ;; *) CSPROJ_ARG="$PROJECT_DIR/$GAME.csproj" ;; esac
# A Linux Godot finds its .NET runtime through DOTNET_ROOT: point it at the SDK in .env when it is not on PATH.
case "$DOTNET" in *.exe|"") ;; *) export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$DOTNET")")}" ;; esac

require_dotnet() {
    [ -n "$DOTNET" ] && [ -x "$DOTNET" ] && return 0
    echo "No .NET SDK (8 or newer): set DOTNET in .env, or install one." >&2
    exit 127
}

# Rebuild only when a source is newer than the assembly: Godot loads a stale one without a word.
build_if_stale() {
    if [ -f "$ASSEMBLY" ]; then
        local newer
        newer="$(find "$PROJECT_DIR/src" "$PROJECT_DIR/tests" "$PROJECT_DIR/addons" "$PROJECT_DIR/project.godot" \
            "$PROJECT_DIR/$GAME.csproj" -newer "$ASSEMBLY" -print -quit 2>/dev/null)"
        [ -z "$newer" ] && return 0
    fi
    require_dotnet
    echo "Sources changed -- building..." >&2
    local out status
    out="$("$DOTNET" build "$CSPROJ_ARG" -v q --nologo 2>&1 | tr -d '\r')"
    status=$?
    if [ "$status" -ne 0 ] || [ ! -f "$ASSEMBLY" ]; then
        printf '%s\n' "$out" | grep -E "error" | sort -u >&2
        echo "build failed" >&2
        exit 1
    fi
    printf '%s\n' "$out" | grep -E ": warning [A-Z]+[0-9]+" | sort -u >&2 || true
}
