#!/usr/bin/env bash
# fixtest2.sh: playtest-2 fixes — lighter per player, nun audio for the target, nun reset after a client death.
cd "$(dirname "$0")"
set -- $(./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 10
L(){ ./nm.sh $1 "find Lighter Item" | grep -v -E 'Alternate|Inside|Top|^>' | sed 's/ yaw=.*//'; }
echo "lighters H:"; L $H; echo "lighters C:"; L $C
./nm.sh $C "interact Lighter Item MP2" | tail -1; sleep 2
echo "after client took MP2 -> H:"; L $H

./nm.sh $H faststart >/dev/null
for i in $(seq 1 40); do s=$(./nm.sh $H state | tail -1); echo "$s" | grep -q -E 'Search|Look and Move|Set Pt|Wait|Ik Look' && ! echo "$s" | grep -q Hidden && break; sleep 3; done
./nm.sh $C "tp nun" "turn 180" >/dev/null
for i in 1 2 3 4 5 6; do sleep 2; ./nm.sh $C "tp nun" >/dev/null
  echo "t$i nun=$(./nm.sh $H state | tail -1 | grep -o 'nun=([^)]*) [A-Za-z ]*' | cut -c1-40) | H audio: $(./nm.sh $H audio | tail -1) | C audio: $(./nm.sh $C audio | tail -1)"; done
./nm.sh $C die >/dev/null; sleep 12
echo "after client death | H audio: $(./nm.sh $H audio | tail -1) | C audio: $(./nm.sh $C audio | tail -1)"
grep -h -E 'poked the nun|extra lighter|nun target' /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log | tail -8
echo "$H $C"
