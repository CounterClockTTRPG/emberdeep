#!/usr/bin/env bash
# Checks the starter end to end: makes a game from the template in a scratch folder, builds it, runs its self-check
# and its screenshot walkthrough, then plays it through godot-agent (a new game, moves, a press, a shot, quit).
# Exercises godot-agent and sfxgen with it. Needs Godot 4.7 .NET and a .NET SDK (see template/.env.example).
#   bin/selftest.sh [SCRATCH_DIR]      (default: a fresh temp folder; it is kept for a look afterwards)
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
scratch="${1:-$(mktemp -d)}"
game="$scratch/gemtest"
rm -rf "$game"
fail=0
step() { printf '\n== %s\n' "$*"; }
ok() { if [ "$1" -eq 0 ]; then echo "   ok"; else echo "   FAILED"; fail=1; fi; }

step "new game"
"$here/bin/new-game.sh" Gemtest "$game" | tail -4; ok "${PIPESTATUS[0]}"
[ -f "$here/template/.env" ] && cp "$here/template/.env" "$game/.env"
cd "$game" || exit 1
grep -rq "Starter" --include=*.cs --include=*.godot --include=*.csproj --include=*.sh . && { echo "   the name was not replaced everywhere"; fail=1; }

step "build"
./play.sh --build; ok $?

step "self-check"
./play.sh --check | tail -8; ok "${PIPESTATUS[0]}"

step "walkthrough"
timeout 180 ./play.sh --shots | tail -5
for s in title game game-moved; do [ -s "shots/$s.png" ] || { echo "   no shots/$s.png"; fail=1; }; done
[ -s shots/game.png ] && echo "   shots: $(ls shots | tr '\n' ' ')"

step "agent"
rm -f agent/out.txt
(./play.sh --agent > "$scratch/agent.log" 2>&1 &)
for _ in $(seq 1 90); do grep -q "^ready" agent/out.txt 2>/dev/null && break; sleep 2; done
grep -q "^ready" agent/out.txt 2>/dev/null || { echo "   the game did not start under the agent"; cat "$scratch/agent.log" | tail -20; exit 1; }
out="$(AGENT_TIMEOUT=120 scripts/agent.sh "ui; new 42; state; key Right; key Right; key Down; state; wait 3500; state; teleport 0 0; at 3 3; shot board; press Title; state; tree 2; perf 30")"
status=$?
printf '%s\n' "$out" | sed 's/^/   /' | head -60
[ $status -eq 0 ] || fail=1
printf '%s\n' "$out" | grep -q "screen: GameScreen; at" || { echo "   'state' did not describe the game"; fail=1; }
printf '%s\n' "$out" | grep -q "pressed 'Title (Esc)'" || { echo "   'press' did not find the button"; fail=1; }
printf '%s\n' "$out" | grep -q "screen: TitleScreen" || { echo "   pressing Title did not go back to the title"; fail=1; }
[ -s agent/board.png ] || { echo "   no agent/board.png"; fail=1; }
AGENT_TIMEOUT=30 scripts/agent.sh "quit" >/dev/null

echo
if [ $fail -eq 0 ]; then echo "starter selftest: all good ($game)"; else echo "starter selftest: FAILED ($game)"; fi
exit $fail
