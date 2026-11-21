#!/usr/bin/env bash
# Make a new Godot C# game from the template.
#   bin/new-game.sh NAME [DIR]       NAME: a C# identifier (Gemrun, Hammerdeep); DIR: default ../<name in lower case>
# It copies template/, renames Starter to NAME everywhere (namespace, assembly, project, scripts), installs
# godot-agent and sfxgen (from sibling folders, or cloned from GitHub), and makes a first git commit.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
name="${1:?usage: new-game.sh NAME [DIR]}"
[[ "$name" =~ ^[A-Z][A-Za-z0-9]*$ ]] || { echo "NAME must start with a capital and be letters and digits: it becomes the C# namespace"; exit 1; }
lower="$(printf '%s' "$name" | tr '[:upper:]' '[:lower:]')"
upper="$(printf '%s' "$name" | tr '[:lower:]' '[:upper:]')"
dest="${2:-$(dirname "$here")/$lower}"
[ -e "$dest" ] && [ -n "$(ls -A "$dest" 2>/dev/null)" ] && { echo "$dest exists and is not empty"; exit 1; }
mkdir -p "$dest"
cp -r "$here/template/." "$dest/"
mv "$dest/Starter.csproj" "$dest/$name.csproj"
# The name, in its three spellings, in every text file.
grep -rlI --exclude-dir=.git -e Starter -e STARTER -e starter "$dest" | while read -r f; do
    # (the repository's own name, godot-starter, stays as it is)
    sed -i -e "s/godot-starter/@@REPO@@/g" -e "s/Starter/$name/g" -e "s/STARTER/$upper/g" -e "s/starter/$lower/g" \
        -e "s/@@REPO@@/godot-starter/g" "$f"
done
# The building blocks: from sibling checkouts when there are some, otherwise from GitHub.
block() {
    local repo="$1" src="$(dirname "$here")/$1"
    if [ ! -x "$src/bin/install.sh" ]; then
        src="$(mktemp -d)/$repo"
        git clone -q --depth 1 "https://github.com/CounterClockTTRPG/$repo.git" "$src"
    fi
    "$src/bin/install.sh" "$dest"
}
block godot-agent
block sfxgen
chmod +x "$dest/play.sh" "$dest/scripts/"*.sh
if [ ! -d "$dest/.git" ]; then
    (cd "$dest" && git init -q -b main && git add -A && git commit -q -m "$name: a new game from godot-starter")
fi
echo
echo "$name is ready in $dest"
echo "  cd $dest && ./play.sh --check     the self-check"
echo "  ./play.sh                         play"
echo "  ./play.sh --agent                 then scripts/agent.sh \"help\""
