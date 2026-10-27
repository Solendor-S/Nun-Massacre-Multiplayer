#!/usr/bin/env bash
# doortest.sh: nun player walks into a real interior door from both sides; does it open (on both screens)?
cd "$(dirname "$0")"
set -- $(MODE=nunplayer NUNSLOT=1 ./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 5
D="Scripted Main Game OFF/_Doors/Right Side/Door right sized (9)/Door Doomster"
Y(){ ./nm.sh $1 "interactables Door right sized (9)" | grep -F "$D" | grep -o 'yaw=[0-9]*'; }
echo "before H:$(Y $H) C:$(Y $C)"
for s in 2.0 -2.0; do
  PL=$(python -I -c "import sys; s=float(sys.argv[1]); print(202.9+s, 0.1, 36.9, 270 if s>0 else 90, 0)" $s)
  ./nm.sh $C "nunplace $PL" >/dev/null; sleep 0.3; ./nm.sh $C "nunwalk 0 1 2.2" >/dev/null; sleep 3
  echo "walked in from x$s: H:$(Y $H) C:$(Y $C)"
done
echo "$H $C"
