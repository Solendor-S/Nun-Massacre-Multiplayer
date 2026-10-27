#!/usr/bin/env bash
# bedtest.sh: co-op AI nun sees the client, client hides under a bed -> she flips it (on both screens) and attacks.
cd "$(dirname "$0")"
set -- $(./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 6
./nm.sh $H faststart >/dev/null
for i in $(seq 1 40); do s=$(./nm.sh $H state | tail -1); echo "$s" | grep -q -E 'Search|Look and Move|Set Pt|Wait|Ik Look' && ! echo "$s" | grep -q Hidden && break; sleep 3; done
B="Geometry/Right/4th Floor/Furniture/Beds/Bed (5)/Bed Offset Parent/bed interactable mesh"
for i in $(seq 1 25); do ./nm.sh $C "tp nun" "facenun" >/dev/null; sleep 1; ./nm.sh $H state | tail -1 | grep -q 'Chase' && break; done
echo "chasing: $(./nm.sh $H state | tail -1 | grep -o 'nun=([^)]*) [A-Za-z ]*target=[a-z0-9 ]*')"
./nm.sh $H "aiwarp 237.8 15.0 30.6" | tail -1; ./nm.sh $C "tp 237.8 15.1 33.0" "facenun" >/dev/null; sleep 0.6
./nm.sh $C "interact $B" | tail -1
for i in $(seq 1 12); do sleep 2; echo "t$i $(./nm.sh $H state | tail -1 | grep -o 'nun=([^)]*) [A-Za-z ]*target=[a-z0-9 ]*') | C $(./nm.sh $C lives | tail -1 | grep -o 'hp=[-0-9]*')"; done
grep -h -E 'hides under|nun flips|left the bed' /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log
echo "$H $C"
