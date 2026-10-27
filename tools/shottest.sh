#!/usr/bin/env bash
# shottest.sh: revolver hits on the nun reach whoever controls her.
# (1) Play as Nun: survivor (host) shoots -> nun player (client) stunned 25 s, powers locked.
# (2) Co-op: client shoots -> host's AI nun reacts (ChaseOff -> Blood -> Retreat).
cd "$(dirname "$0")"
S="${NUNMP_SHOTS:-$(cygpath -w "$PWD/../shots")}"; mkdir -p "$(cygpath -u "$S")"  # screenshots (Windows path for the game)
set -- $(MODE=nunplayer NUNSLOT=1 ./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 10
echo "survivor's copy of the nun has colliders: $(./nm.sh $H 'colliders Characters/Killer Stuff new/Killer' | grep -c 'trig=False')"
G(){ ./nm.sh $C nun | tail -1 | grep -o 'shot=[0-9]*'; }
T(){ ./nm.sh $C nun >/dev/null; grep -c 'Trap set' /dev/null; }
echo "before: $(G)"
./nm.sh $H shootnun | tail -1; sleep 0.4; ./nm.sh $C "shot $S\\nunshot.png" >/dev/null; sleep 1
echo "after:  $(G)"
./nm.sh $C "nunkey T" >/dev/null; sleep 1
echo "trap while shot (want none): $(grep -h -c 'NunMP Trap' /dev/null) traps=$(./nm.sh $H 'find NunMP Trap' | grep -vc '^>')"
grep -h -E 'I shot the nun|I was shot' /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log | tail -2

set -- $(./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 6
./nm.sh $H faststart >/dev/null; sleep 10
F(){ ./nm.sh $H "fsm Killer Stuff new/Killer" | tail -1; }
echo "host AI before: $(F)"
./nm.sh $C shootnun | tail -1; sleep 1.5
echo "host AI after:  $(F)"
grep -h -E 'shot the nun|Damaged is true|Damage Check' /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log | tail -4
grep -h -E 'Exception' /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log | sort | uniq -c | head -5
echo "$H $C"
