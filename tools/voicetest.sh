#!/usr/bin/env bash
# voicetest.sh: Opus voice peer to peer. Client sends a 440 Hz tone through the real Opus path next to the host,
# then from 60 m away (proximity cull: nothing sent), then host -> client. Prints bytes sent + receiver stats.
cd "$(dirname "$0")"
set -- $(./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 8
pos(){ ./nm.sh $1 state | tail -1 | grep -o 'player=([^)]*)' | tr -d 'player=(),'; }
HP=$(pos $H); echo "host at $HP"
./nm.sh $C "tp $HP" >/dev/null; sleep 2
B(){ ./nm.sh $1 voice | tail -1 | grep -o 'sent=[0-9]*' | tr -d 'sent='; }
# talk: 3 s tone; prints bytes/s sent and the listener's output level mid-sentence
talk(){ local L=$2; local b0=$(B $1); ./nm.sh $1 "opustone 3" "testptt 1" >/dev/null; sleep 2
  echo "  mid-talk $L hears: $(./nm.sh $L voice | tail -1 | grep -o 's[0-9]:[^|]*' | grep -v ':quiet' | head -1 | grep -o 'buf=[0-9]*ms\|gaps=[0-9]*\|out=[0-9.]*' | tr '
' ' ')"
  sleep 1; ./nm.sh $1 "testptt 0" >/dev/null; local b1=$(B $1); echo "  sent $(( (b1 - b0) / 3 )) B/s"; sleep 1; }
talk $C $H
echo "NEAR  C: $(./nm.sh $C voice | tail -1 | cut -c1-60)"
echo "NEAR  H hears: $(./nm.sh $H voice | tail -1 | grep -o 's[0-9]:[^|]*' | head -1)"
FAR=$(python -I -c "import sys; x,y,z=map(float,sys.argv[1:4]); print(x+60, y, z)" $HP)
./nm.sh $C "tpraw $FAR" >/dev/null; sleep 2
talk $C $H
echo "FAR   C: $(./nm.sh $C voice | tail -1 | cut -c1-60)"
echo "FAR   H hears: $(./nm.sh $H voice | tail -1 | grep -o 's[0-9]:[^|]*' | head -1)"
./nm.sh $C "tp $HP" >/dev/null; sleep 2
talk $H $C
echo "H->C  H: $(./nm.sh $H voice | tail -1 | cut -c1-60)"
echo "H->C  C hears: $(./nm.sh $C voice | tail -1 | grep -o 's0:[^|]*' | head -1)"
grep -h -E 'Exception|handler Voice' /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log | sort | uniq -c | head -5
echo "$H $C"
