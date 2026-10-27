#!/usr/bin/env bash
# lurktest.sh: (1) Play as Nun: nun stands still 3 s -> lurking, FOV narrows, survivor next to her loses the static;
# moves -> static back. (2) Co-op: client dies -> respawn NunReset -> AI nun runs to her retreat, no teleport.
cd "$(dirname "$0")"
S="${NUNMP_SHOTS:-$(cygpath -w "$PWD/../shots")}"; mkdir -p "$(cygpath -u "$S")"  # screenshots (Windows path for the game)
L(){ ./nm.sh $1 nun | tail -1 | grep -o 'sneak=[A-Za-z]* lurk=[A-Za-z]* fov=[0-9]*'; }
set -- $(MODE=nunplayer NUNSLOT=1 ./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 10
./nm.sh $C "nunwalk 0 1 6" >/dev/null; sleep 1; ./nm.sh $H "tp nun" >/dev/null; sleep 1
echo "walking:     nun $(L $C) | survivor $(./nm.sh $H tape | tail -1)"
sleep 4.5; ./nm.sh $H "tp nun" >/dev/null; sleep 3
echo "still 3s:    nun $(L $C) | survivor $(./nm.sh $H tape | tail -1)"
./nm.sh $C "shot $S\lurkview.png" >/dev/null
./nm.sh $C "nunwalk 1 0 3" >/dev/null; sleep 1.2
echo "moving, near: nun $(L $C) | survivor $(./nm.sh $H tape | tail -1)"
FAR=$(python -I -c "import sys; x,y,z=map(float,sys.argv[1:4]); print(x+40, y, z)" $(./nm.sh $H state | tail -1 | grep -o 'player=([^)]*)' | tr -d 'player=(),'))
./nm.sh $C "nunwalk 0 0 0.1" >/dev/null; sleep 3.5; ./nm.sh $H "tpraw $FAR" >/dev/null; sleep 1; ./nm.sh $C "nunwalk 1 0 1" >/dev/null; sleep 1.5
echo "left while she lurked, then she moved: survivor $(./nm.sh $H tape | tail -1) (want False)"

# ---- co-op AI nun retreat
set -- $(./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 6
./nm.sh $H faststart >/dev/null; sleep 8
NP(){ ./nm.sh $H nun | tail -1 | grep -o 'nunpos=([^)]*)' | tr -d 'nunpos=(),'; }
echo "nun at $(NP) before client death"
./nm.sh $C die >/dev/null
prev=$(NP); maxjump=0
for i in $(seq 1 40); do sleep 0.5; cur=$(NP)
  j=$(python -I -c "import sys,math; a=list(map(float,sys.argv[1:4])); b=list(map(float,sys.argv[4:7])); print(round(math.dist(a,b),1))" $prev $cur 2>/dev/null || echo 0)
  maxjump=$(python -I -c "import sys; print(max(float(sys.argv[1]),float(sys.argv[2])))" $maxjump $j); prev=$cur
done
echo "nun at $(NP) after; biggest jump in 0.5 s = $maxjump m (warp would be many m; running ~3 m)"
grep -h -E 'nun retreat|NunReset' /c/Games/NunMassacre-MP/player.log | tail -3
grep -h -E 'Exception' /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log | sort | uniq -c | head -5
echo "$H $C"
