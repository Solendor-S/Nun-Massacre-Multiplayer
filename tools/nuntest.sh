#!/usr/bin/env bash
# nuntest.sh: Play-as-Nun smoke test. Client (slot 1) is the nun.
cd "$(dirname "$0")"
set -- $(MODE=nunplayer NUNSLOT=1 ./mp2.sh 2>/dev/null | tail -1); H=$1; C=$2; sleep 6
echo "H: $(./nm.sh $H nun | tail -1)"; echo "C: $(./nm.sh $C nun | tail -1)"
./nm.sh $C "nunwalk 0 1 3" >/dev/null; sleep 3.5
echo "after walk  H sees nun $(./nm.sh $H nun | tail -1 | grep -o 'nunpos=.*') | C $(./nm.sh $C nun | tail -1 | grep -o 'nunpos=.*')"
./nm.sh $H "tp nun" >/dev/null; sleep 1.5
echo "aim: $(./nm.sh $C nun | tail -1 | grep -o 'aim=[0-9]*')  host hp before: $(./nm.sh $H lives | tail -1 | grep -o 'hp=[-0-9]*')"
./nm.sh $C "nunkey Mouse0" >/dev/null; sleep 2
echo "host hp after swing: $(./nm.sh $H lives | tail -1 | grep -o 'hp=[-0-9]*')"
./nm.sh $C "nunkey T" >/dev/null; sleep 1
echo "traps H=$(./nm.sh $H "find NunMP Trap" | grep -c "active=") C=$(./nm.sh $C "find NunMP Trap" | grep -c "active=")"
./nm.sh $C "nunkey R" >/dev/null; sleep 1; ./nm.sh $C "nunkey F" >/dev/null; sleep 1; ./nm.sh $C "nunkey G" >/dev/null; sleep 1
echo "H audio after scream: $(./nm.sh $H audio | tail -1)"
grep -h -E 'I am the nun|nun AI off|nun puppeted|Exception|no bear trap' /c/Games/NunMassacre-MP/player.log /c/Games/NunMassacre-MP2/player.log | sort | uniq -c | head
echo "$H $C"
