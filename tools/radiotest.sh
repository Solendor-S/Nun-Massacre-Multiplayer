#!/usr/bin/env bash
# radiotest.sh: co-op - radios spawn identically, pickup syncs, radio voice, and the death cue.
cd "$(dirname "$0")"
set -- $(./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 6
S="${NUNMP_SHOTS:-$(cygpath -w "$PWD/../shots")}"; mkdir -p "$(cygpath -u "$S")"  # screenshots (Windows path for the game)
echo "radios H: $(./nm.sh $H 'find NunMP Radio' | grep -o 'NunMP Radio [0-9] active=[A-Za-z]* pos=([^)]*)' | tr '\n' ';')"
echo "radios C: $(./nm.sh $C 'find NunMP Radio' | grep -o 'NunMP Radio [0-9] active=[A-Za-z]* pos=([^)]*)' | tr '\n' ';')"
./nm.sh $C "takeradio 0" | tail -1; sleep 1
echo "after client took radio 0, host: $(./nm.sh $H voice | tail -1 | grep -o 'radios=[0-9,]*')"
./nm.sh $H "takeradio 1" >/dev/null
# far apart so the handset (not the real voice) plays
./nm.sh $C "tp 202 0 36.5" >/dev/null; sleep 1
./nm.sh $C "radiotone 3" >/dev/null; sleep 1.5
echo "host hears: $(./nm.sh $H voice | tail -1)"
grep -h -E 'client noise' /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log | tail -1
# death cue: client next to host, host looking at them
./nm.sh $C "tp 0" >/dev/null; sleep 1; ./nm.sh $H "face 1" >/dev/null
./nm.sh $C die >/dev/null; sleep 0.6; ./nm.sh $H "shot $S\\death1.png" >/dev/null; sleep 1.2; ./nm.sh $H "shot $S\\death2.png" >/dev/null; sleep 1
echo "$H $C"
