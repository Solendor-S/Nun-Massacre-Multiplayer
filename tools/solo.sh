#!/usr/bin/env bash
# solo.sh: rebuild, launch 1 instance, go straight into the game scene. Prints pid last.
cd "$(dirname "$0")"
P=$(./relaunch.sh 1 | tail -1)
timeout 90 ./tomenu.sh $P >/dev/null
./nm.sh $P "click Main/GoToPlaySettings" >/dev/null; sleep 1
./nm.sh $P "click Holder (1)/Skip" >/dev/null; sleep 30
./nm.sh $P state >&2
echo $P
