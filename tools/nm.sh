#!/usr/bin/env bash
# nm.sh <pid> "cmd1" "cmd2" ...  -> sends bridge commands to a running NunMP game, prints reply.
# Finds the install the pid runs from via its nunmp_out_<pid>.txt marker (dev uses two lab copies).
pid="$1"; shift
G=""
for d in /c/Games/NunMassacre-MP /c/Games/NunMassacre-MP2; do [ -f "$d/BepInEx/nunmp_out_$pid.txt" ] && G="$d/BepInEx"; done
[ -z "$G" ] && { echo "no bridge marker for pid $pid"; exit 1; }
out="$G/nunmp_out_$pid.txt"; : > "$out"
printf '%s\n' "$@" > "$G/nunmp_cmd_$pid.txt"
for i in $(seq 1 40); do
  [ -f "$G/nunmp_cmd_$pid.txt" ] || { sleep 0.3; cat "$out"; exit 0; }
  sleep 0.25
done
echo "timeout (game not running / paused?)"
