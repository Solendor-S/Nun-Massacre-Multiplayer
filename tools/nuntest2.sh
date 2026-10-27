#!/usr/bin/env bash
# nuntest2.sh: nun-mode feedback round 2 — physics walk, crouch, sneak static, drag miss cooldown, jumpscare notice, peek.
cd "$(dirname "$0")"
set -- $(MODE=nunplayer NUNSLOT=1 ./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 6
S="${NUNMP_SHOTS:-$(cygpath -w "$PWD/../shots")}"; mkdir -p "$(cygpath -u "$S")"  # screenshots (Windows path for the game)
P(){ ./nm.sh $1 nun | tail -1 | grep -o 'nunpos=.*'; }
echo "start  C $(P $C)"; ./nm.sh $C "nunwalk 0 1 3" >/dev/null; sleep 3.5; echo "walked C $(P $C) | H $(P $H)"
# survivor right in front of her: static on, then she crouches -> static off on the survivor's screen
./nm.sh $H "tp nun" >/dev/null; sleep 2; echo "survivor near, standing: $(./nm.sh $H tape | tail -1)"
./nm.sh $C "nuncrouch 1" >/dev/null; sleep 1.5; echo "she crouches: $(./nm.sh $H tape | tail -1) | sneak seen by host: $(./nm.sh $H nun | tail -1 | grep -o 'sneak=[A-Za-z]*')"
./nm.sh $C "shot $S\\crouchview.png" >/dev/null; ./nm.sh $C "nuncrouch 0" >/dev/null
# jumpscare notice: survivor looking at her triggers the sting -> nun told
./nm.sh $H "facenun" >/dev/null; sleep 3
grep -h -E "SAW YOU|jumpscare" /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log | tail -2
echo "nun audio: $(./nm.sh $C audio | tail -1)"
# drag with nobody there -> 30 s cooldown
./nm.sh $C "nunkey Mouse1" >/dev/null; sleep 1
# peek right, survivor screenshots her body; nun screenshots her view (swing cooldown too)
./nm.sh $H "tp nun" "facenun" >/dev/null; ./nm.sh $C "nunlean 1" >/dev/null; sleep 1; ./nm.sh $H "shot $S\\peekbody.png" >/dev/null; ./nm.sh $C "nunkey Mouse0" >/dev/null; sleep 0.3; ./nm.sh $C "shot $S\\nunhud.png" >/dev/null; sleep 1
grep -h -E 'Exception' /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log | sort | uniq -c | head -5
echo "$H $C"
