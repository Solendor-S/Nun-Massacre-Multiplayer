#!/usr/bin/env bash
# package.sh: build the friend zip -> NunMassacreMP/dist/NunMP-<ver>.zip (no game files, no UnityExplorer, no PDM export)
cd "$(dirname "$0")/.."
VER=$(grep -o 'Version = "[0-9.]*"' src/Net/Session.cs | grep -o '[0-9.]*')
(cd src && "/c/Program Files/dotnet/dotnet.exe" build -c Release 2>&1 | grep -E ' error |Build succeeded' | sort -u) || exit 1
OUT=dist/NunMP-$VER; rm -rf "$OUT"; mkdir -p "$OUT/game/BepInEx/plugins/NunMP" "$OUT/game/BepInEx/core"
BEP="${BEPINEX_DIR:-.cache/bepinex}"   # BepInEx 5.4.23.6 x64, downloaded once
[ -d "$BEP" ] || { mkdir -p "$BEP" && curl -sL -o "$BEP.zip" https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_win_x64_5.4.23.6.zip && unzip -q "$BEP.zip" -d "$BEP"; }
cp -r "$BEP/." "$OUT/game/"
cp src/bin/Release/net48/NunMP.dll src/lib/Steamworks.NET.dll src/lib/Concentus.dll src/lib/steam_api64.dll "$OUT/game/BepInEx/plugins/NunMP/"
cp tools/concentus/LICENSE "$OUT/game/BepInEx/plugins/NunMP/Concentus-LICENSE.txt"
cp dist-src/install.bat dist-src/install.ps1 dist-src/README.txt tools/pdm_export.py "$OUT/"
# python zipfile: PS 5.1 Compress-Archive writes backslash paths that non-Explorer unzippers mangle
(cd dist && rm -f "NunMP-$VER.zip" && python -I -c "import shutil,sys; shutil.make_archive(sys.argv[1], 'zip', sys.argv[1])" "NunMP-$VER")
ls -la "dist/NunMP-$VER.zip"; find "$OUT" -type f | sed "s#$OUT/##"
