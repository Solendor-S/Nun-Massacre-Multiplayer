#!/usr/bin/env bash
# fixtest.sh: playtest-1 regressions — doors both ways, personal lighter, plank over barbed wire, barbed-wire pit.
cd "$(dirname "$0")"
set -- $(./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 12
D="Entrance Door/Door Doomster"
Y(){ ./nm.sh $1 "find Door Doomster" | grep -F "$D" | grep -o 'yaw=[0-9]*'; }
./nm.sh $H "push $D|70" >/dev/null; sleep 3; ./nm.sh $H "tp 238.6 -1.4 26.7" >/dev/null; echo "door host push   H:$(Y $H) C:$(Y $C)"
./nm.sh $C "push $D|-50" >/dev/null; sleep 3; ./nm.sh $C "tp 238.6 -1.4 26.7" >/dev/null; echo "door client push H:$(Y $H) C:$(Y $C)"

act(){ ./nm.sh $1 "find $2" | grep -v -E 'Alternate|Lighter (Inside|Top)' | head -1 | grep -o 'active=[A-Za-z]*'; }
./nm.sh $C "interact Lighter Item" | tail -1; sleep 2
echo "lighter H:"; ./nm.sh $H "find Lighter Item" | grep -v -E 'Alternate|Inside|Top|^>'
echo "lighter C:"; ./nm.sh $C "find Lighter Item" | grep -v -E 'Alternate|Inside|Top|^>'

./nm.sh $H "interact Wood Plank Puzzle/Wooden Plank Item" | tail -1; sleep 3
./nm.sh $H "interact Scripted Main Game OFF/Wood Plank Puzzle/Barbed Wire Puzzle (1)" | tail -1; sleep 4
SQ="get Scripted Main Game OFF/Wood Plank Puzzle/Barbed Wire Puzzle (1)|StateInteractable|stateIndexCurrent"
echo "barbed state H=$(./nm.sh $H "$SQ" | tail -1) C=$(./nm.sh $C "$SQ" | tail -1)"
echo "plank H: $(./nm.sh $H 'find Wooden Plank Item' | grep -v Alternate)"
echo "plank C: $(./nm.sh $C 'find Wooden Plank Item' | grep -v Alternate)"

echo "lives before pit: $(./nm.sh $C lives | tail -1)"
./nm.sh $C "call Scripted Main Game OFF/Wood Plank Puzzle/Barbed Wire Puzzle (1)/Ending trigger|FSMOwner|StartBehaviour" | tail -1; sleep 12
echo "lives after pit:  $(./nm.sh $C lives | tail -1)"
grep -h -E 'barbed wire pit|remote state|send state|Exception' /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log | tail -8
echo "$H $C"
