#!/usr/bin/env bash
# wtest.sh "<interactable path suffix>" ["<component>|<field>"]: client interacts; compare the field on both peers.
cd "$(dirname "$0")"
set -- "$1" "${2:-StateInteractable|stateIndexCurrent}" $(./mp2.sh | tail -1)
OBJ=$1; FIELD=$2; H=$3; C=$4
sleep 12
L1=/c/Games/NunMassacre-MP/player.log; L2=/c/Games/NunMassacre-MP2/player.log
echo "idle sends after settle: $(grep -h -c -E 'send (state|fsm)' $L1 $L2 | tr '\n' ' ')"
Q="get $OBJ|$FIELD"
echo "before H=$(./nm.sh $H "$Q" | tail -1) C=$(./nm.sh $C "$Q" | tail -1)"
./nm.sh $C "interact $OBJ" | tail -1
sleep 3
echo "after  H=$(./nm.sh $H "$Q" | tail -1) C=$(./nm.sh $C "$Q" | tail -1)"
grep -h -E 'send (state|fsm)|remote (state|fsm)|Exception' $L1 $L2 | tail -12
echo "$H $C"
