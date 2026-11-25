#!/usr/bin/env bash
# Send commands to a game running a godot-agent AgentServer and print its reply.
#   agent.sh "state"
#   agent.sh "press Start; key Space; shot title"
# Commands are separated by ';'; 'help' lists them. The game's agent folder is ./agent (run from the project), or
# AGENT_DIR=..., or --dir DIR as the first argument. Waits up to AGENT_TIMEOUT seconds (default 300).
set -uo pipefail
dir="${AGENT_DIR:-$PWD/agent}"
if [ "${1:-}" = "--dir" ]; then dir="$2"; shift 2; fi
[ $# -gt 0 ] || { sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//'; exit 2; }
mkdir -p "$dir"
id="$(date +%s%N)"
printf '%s\n%s\n' "$id" "$*" > "$dir/cmd.tmp"
mv -f "$dir/cmd.tmp" "$dir/cmd.txt"
limit=$(( ${AGENT_TIMEOUT:-300} * 5 ))
for _ in $(seq 1 "$limit"); do
    if [ -f "$dir/out.txt" ] && [ "$(head -n 1 "$dir/out.txt")" = "$id" ]; then
        tail -n +2 "$dir/out.txt"
        grep -q "^error in '" "$dir/out.txt" && exit 1
        exit 0
    fi
    sleep 0.2
done
echo "no reply in ${AGENT_TIMEOUT:-300} s -- is the game running with its agent server, and is $dir its agent folder?"
exit 1
