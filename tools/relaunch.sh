#!/usr/bin/env bash
# Kill running game instances, build+deploy plugin, launch N windowed instances (default 1). Prints PIDs.
# Instance 1 runs from NunMassacre-MP, instance 2 from NunMassacre-MP2: two games must not share a BepInEx folder.
cd "$(dirname "$0")/.."
n="${1:-1}"
for p in $(tools/gamepids.sh); do um win kill "$p" >/dev/null 2>&1; done
sleep 2
B=$(cd src && "/c/Program Files/dotnet/dotnet.exe" build -c Release 2>&1 | grep -E " error |Build succeeded" | sort -u); echo "$B" >&2; echo "$B" | grep -q " error " && { echo "BUILD FAILED" >&2; exit 1; }
mkdir -p /c/Games/NunMassacre-MP2/BepInEx/plugins/NunMP
cp -r /c/Games/NunMassacre-MP/BepInEx/plugins/NunMP/. /c/Games/NunMassacre-MP2/BepInEx/plugins/NunMP/
for i in $(seq 1 "$n"); do
  d=/c/Games/NunMassacre-MP; [ "$i" = 2 ] && d=/c/Games/NunMassacre-MP2
  rm -f "$d"/BepInEx/nunmp_out_* "$d"/BepInEx/nunmp_cmd_*
  "$d/Nun Massacre.exe" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "$(cygpath -w "$d")\\player.log" >/dev/null 2>&1 &
  sleep 3
done
sleep 12
tools/gamepids.sh
