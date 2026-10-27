#!/usr/bin/env bash
# pingtest.sh: nun's ( ! ) noise pings unlock only after 90 s with no survivor seeing her.
cd "$(dirname "$0")"
set -- $(MODE=nunplayer NUNSLOT=1 ./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 10
G(){ ./nm.sh $C nun | tail -1 | grep -o 'seenAgo=[0-9]* pings=[A-Za-z]*'; }
./nm.sh $H "tp nun" "facenun" >/dev/null; sleep 2
echo "survivor looking at her: $(G)  (survivor SeesNun: $(./nm.sh $H nun | tail -1 | grep -o 'sees=[A-Za-z]*'))"
./nm.sh $H "turn 180" >/dev/null; sleep 5
echo "survivor turned away 5s: $(G)"
sleep 88; echo "after ~93 s unseen:      $(G)"
./nm.sh $H "facenun" >/dev/null; sleep 2; echo "seen again:              $(G)"
echo "$H $C"
