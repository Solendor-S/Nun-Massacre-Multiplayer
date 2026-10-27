#!/usr/bin/env bash
# mp2.sh: fresh 2-instance loopback session in game. Prints "HOST CLIENT" pids on the last line.
cd "$(dirname "$0")"
set -- $(./relaunch.sh 2 | tail -2)
A=$1; B=$2
timeout 90 ./tomenu.sh $A & timeout 90 ./tomenu.sh $B & wait
for try in 1 2 3; do
  ./nm.sh $A host >/dev/null; sleep 1; ./nm.sh $B join >/dev/null; sleep 2
  ./nm.sh $B session | grep -q 'connected as' && break
  echo "join retry $try" >&2
done
[ -n "$RULES" ] && ./nm.sh $A "rules $RULES" >&2
[ -n "$MODE" ] && ./nm.sh $A "mode $MODE" "nunslot ${NUNSLOT:-1}" >&2
./nm.sh $A start >/dev/null; sleep 32
./nm.sh $A state | tail -1 | cut -c1-90 >&2
echo "$A $B"
