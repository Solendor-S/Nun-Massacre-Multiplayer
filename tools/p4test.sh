#!/usr/bin/env bash
# p4test.sh [samples]: client teleports in front of the nun once she is searching; does she retarget, chase, hit?
cd "$(dirname "$0")"
./mp2.sh >/dev/null
for p in $(./gamepids.sh); do ./nm.sh $p session | sed -n 2p | grep -q hosting && H=$p || C=$p; done
echo "H=$H C=$C"
./nm.sh $H faststart >/dev/null
for i in $(seq 1 60); do
  s=$(./nm.sh $H state | tail -1)
  echo "$s" | grep -q -E 'Search|Look and Move|Set Pt|Wait|Ik Look' && ! echo "$s" | grep -q 'Hidden' && break
  sleep 3
done
echo "searching: $(echo "$s" | grep -o 'nun=.*')"
./nm.sh $C "tp nun" "turn 180" | grep at; [ -n "$FACE" ] && ./nm.sh $C "turn 180" >/dev/null
for i in $(seq 1 ${1:-15}); do
  sleep 2; [ -n "$FOLLOW" ] && ./nm.sh $C "tp nun" >/dev/null
  echo "hp H=$(./nm.sh $H "get Characters/Player|fpsHealth|healthMeter" | tail -1) C=$(./nm.sh $C "get Characters/Player|fpsHealth|healthMeter" | tail -1)"; echo "H $(./nm.sh $H state | tail -1 | grep -o 'nun=.*') | C $(./nm.sh $C state | tail -1 | grep -o 'player=([^)]*)')"
done
grep -h -E 'nun target|nun hit|Exception' /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log | head -20
echo "$H $C"
