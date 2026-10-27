#!/usr/bin/env bash
# pulltest.sh: host hides under a bed; client-nun drags them out (E) and screenshots her view.
cd "$(dirname "$0")"
set -- $(MODE=nunplayer NUNSLOT=1 ./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 6
B="Bed (5)/Bed Offset Parent/bed interactable mesh"
./nm.sh $H "interact $B" | tail -1; sleep 4
N="get Characters/Player|PlayerStates|isNormal"
echo "host normal (false = hiding): $(./nm.sh $H "$N" | tail -1)"
HP=$(./nm.sh $H state | tail -1 | grep -o 'player=([^)]*)' | tr -d 'player=(),')
echo "host hiding at $HP"
PL=$(python -I -c "import sys; x,y,z=map(float,sys.argv[1:4]); print(x, y, z+1.6, 180, 40)" $HP)
./nm.sh $C "nunplace $PL" >/dev/null; sleep 1
./nm.sh $C "nunkey E" >/dev/null; sleep 4
echo "host normal after nun's E: $(./nm.sh $H "$N" | tail -1)"
S="${NUNMP_SHOTS:-$(cygpath -w "$PWD/../shots")}"; mkdir -p "$(cygpath -u "$S")"  # screenshots (Windows path for the game)
./nm.sh $C "shot $S\\nunview3.png" >/dev/null; sleep 1
grep -h -E 'dragged me out' /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log
echo "$H $C"
