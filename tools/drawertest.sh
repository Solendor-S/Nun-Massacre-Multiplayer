#!/usr/bin/env bash
# drawertest.sh: lighters + radios in the lighter cabinet (co-op, 2 players), then the nun-mode jumpscare facing test.
cd "$(dirname "$0")"
set -- $(./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 6
S="${NUNMP_SHOTS:-$(cygpath -w "$PWD/../shots")}"; mkdir -p "$(cygpath -u "$S")"  # screenshots (Windows path for the game)
for P in $H $C; do
  echo "== $P"; ./nm.sh $P "find Lighter Item" | grep -v -E 'Alternate|Inside|Top|^>' | sed 's/ yaw=.*//'
  ./nm.sh $P "find NunMP Radio" | grep -v '^>' | sed 's/ yaw=.*//'
done
grep -h -E 'extra lighter|radio\(s\)' /c/Games/NunMassacre-MP/player.log | tail -2
# open both drawers and look in from the front
./nm.sh $H "interact Get Lighter cabinet9/cabinet9(5)" "interact Get Lighter cabinet9/cabinet9(6)" | grep -o 'interacted.*'
sleep 1.5
./nm.sh $H "tpraw 229.2 5.4 34.3" >/dev/null; ./nm.sh $H "turn 0" >/dev/null
./nm.sh $H "call Characters/Player|Transform|" >/dev/null 2>&1
echo "$H $C"
