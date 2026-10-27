#!/usr/bin/env bash
# tomenu.sh <pid>: press through splash screens until the main menu PLAY button exists.
cd "$(dirname "$0")"
for i in $(seq 1 40); do
  out=$(./nm.sh "$1" buttons)
  echo "$out" | grep -q 'Main/GoToPlaySettings' && { echo "menu ready ($1)"; exit 0; }
  echo "$out" | grep -q 'Panel/ACCEPT' && ./nm.sh "$1" "click Panel/EN" "click Panel/ACCEPT" >/dev/null
  ./nm.sh "$1" press >/dev/null
  sleep 1.5
done
echo "menu not reached ($1)"; exit 1
