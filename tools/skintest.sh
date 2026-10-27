#!/usr/bin/env bash
# skintest.sh: Play-as-Nun - client nun picks the Driller skin in the lobby; demon voice; crouch-run speed.
cd "$(dirname "$0")"
set -- $(./relaunch.sh 2 | tail -2); A=$1; B=$2
timeout 90 ./tomenu.sh $A >/dev/null & timeout 90 ./tomenu.sh $B >/dev/null & wait
for try in 1 2 3; do ./nm.sh $A host >/dev/null; sleep 1; ./nm.sh $B join >/dev/null; sleep 2; ./nm.sh $B session | grep -q 'connected as' && break; done
./nm.sh $A "mode nunplayer" "nunslot 1" >/dev/null; sleep 1
./nm.sh $B "pref 3" | tail -1; sleep 1          # 3 = SkinID.PDM (Driller)
echo "host rule after client pick: $(./nm.sh $A skin | tail -1)"
./nm.sh $A start >/dev/null; sleep 32
echo "H: $(./nm.sh $A skin | tail -1)"; echo "C: $(./nm.sh $B skin | tail -1)"
S="${NUNMP_SHOTS:-$(cygpath -w "$PWD/../shots")}"; mkdir -p "$(cygpath -u "$S")"  # screenshots (Windows path for the game)
./nm.sh $A "tp nun" "facenun" >/dev/null; sleep 1; ./nm.sh $A "shot $S\\driller.png" >/dev/null
# demon voice: nun player sends a tone, survivor's speaker for her should be in demon mode
./nm.sh $B "tone 2" >/dev/null; sleep 1; echo "survivor hears nun: $(./nm.sh $A voice | tail -1)"
# crouch-run speed
./nm.sh $B "nuncrouch 1" >/dev/null; P0=$(./nm.sh $B nun | tail -1 | grep -o 'nunpos=([^)]*)')
./nm.sh $B "nunwalk 0 1 2" >/dev/null; sleep 2.3; P1=$(./nm.sh $B nun | tail -1 | grep -o 'nunpos=([^)]*)'); echo "crouch walk 2s: $P0 -> $P1"
echo "$A $B"
