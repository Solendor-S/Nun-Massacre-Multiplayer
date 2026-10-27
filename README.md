# Nun Massacre Multiplayer

An unofficial multiplayer mod for **Nun Massacre** (Puppet Combo). Survive the nun together with 2–8 friends,
talk over proximity voice, or let one of you **play as the nun** and hunt everyone else.

Runs over Steam: no port forwarding, no server to host.

> Not affiliated with or endorsed by Puppet Combo. No game files are included. You need your own copy of the game.

## Features

### Co-op Survival (2–8 players vs the AI nun)
- She hunts whoever she sees or hears, otherwise the closest player. She can drag remote players out from under beds too.
- **Shared world:** doors, locks, puzzles, items, lights, cabinets and glass are shared. Inventories are your own.
- **One lighter and one walkie-talkie per survivor**, waiting in the lighter's drawer.
- **The radar** shows your teammates as steady dots (the nun is the blinking one).
- **The revolver** works for everyone: shoot her and she bleeds, flees and is blind for a minute.
- **Rules panel for the host:**
  - lives count;
  - a shared pool or lives for each player;
  - what happens when a player runs out of lives: they spectate, or it's game over for everyone.
- When someone is caught, everyone sees them collapse. Anyone escaping gets everyone the ending.

### Play as Nun (1 nun vs the rest)
You play her in first person, with dark vision, a sprint, a crouch-sneak that fits through vents, and corner peeking.

- **Your powers:**
  - melee swing;
  - drag survivors out of beds and cabinets;
  - bear traps;
  - poison gas in the vents;
  - a scream that tears the screen and drains stamina;
  - lights out.
- **Stealth:**
  - Sneak or stand still to **lurk**. Survivors near you don't get the screen static.
  - Noise pings ( ! ) show up only after no one has seen you for 90 s.
- **Looks:** pick Nun, Clown, Backrooms or Driller.
- **Your voice** reaches survivors deep, distorted and echoing.
- **Revolver hits** stagger you and lock your powers for 25 s.

### Proximity voice
- **Push-to-talk on V**, or open mic.
- **3D positioned:** voices are muffled through walls and echo like the room the speaker is in.
- **Walkie-talkies:** hold **B** to reach everyone else carrying one. Careful, the nun can hear them crackle.
- **Caught players** can only be heard by other spectators.
- **The nun can hear you talk** (host rule): whisper near her and she may come.
- **Mic tools:** auto boost for quiet laptop mics, mic test meter, device picker.
- **Under the hood:** Opus-compressed voice goes peer to peer through Steam's relays, so nobody's IP address is shown.

## Requirements
- **Nun Massacre Definitive Edition**, itch.io build **2022.06.08** (Windows). **Every player needs this exact build.**
  The mod checks it and refuses to connect mismatched games.
- **Steam** running and logged in.
  - You'll show as *playing Spacewar*. That's normal: the mod uses Steam's free test app (AppID 480) for lobbies
    and relayed connections.
  - You don't need to own Nun Massacre on Steam.
- **Everyone on the same mod version.**
- Optional: **Power Drill Massacre 0.04** (itch.io) and **Python 3**, to make teammates look like PDM's survivor
  instead of a placeholder. Each player extracts the model from their *own* copy; no PDM files are shipped.

## Install
1. Download `NunMP-<version>.zip` from the **[latest release](https://github.com/Solendor-S/Nun-Massacre-Multiplayer/releases/latest)**.
2. Unzip it anywhere and double-click **`install.bat`**.
3. Pick your Nun Massacre folder (the one with `Nun Massacre.exe`).
4. Answer the Power Drill Massacre question: *y* if you have it, then pick its folder.

The installer adds [BepInEx 5](https://github.com/BepInEx/BepInEx) and the mod to your game folder.

> Tip: install into a **copy** of your game folder if you want to keep a clean vanilla copy.

**Windows blocked the script?** Run it by hand from the unzipped folder:
`powershell -ExecutionPolicy Bypass -File .\install.ps1`

## How to play
1. Start Steam, then launch Nun Massacre. The multiplayer panel opens on the main menu (**F2** toggles it anytime).
2. **Host:** pick a gamemode, set the rules, click **Host game**.
3. **Friends:** click **Join &lt;host name&gt;**, or paste the host's lobby code and click **Join**.
4. In *Play as Nun*, the host picks who the nun is (or Random), and the nun player picks her look.
5. The host sets the normal Play options (difficulty etc.) and clicks **START GAME**. Everyone is taken in together.

### Controls
| | |
|---|---|
| **F2** | multiplayer panel (lobby, rules, voice settings, mic test) |
| **V** | push-to-talk |
| **B** | talk on the walkie-talkie (once you've picked one up) |

As the nun:

| | |
|---|---|
| **WASD** | move |
| **Shift** | sprint (stamina; run it dry and you're winded until it refills a fifth) |
| **Ctrl / C** | crouch-sneak (fits vents, no screen static). **Shift while crouched** = crouch-run (faster, gives you away) |
| *stand still ~2.5 s* | lurk (your view narrows; survivors near you get no static) |
| **Q / E** | peek left / right |
| **Left click** | swing (40 damage) |
| **Right click** | drag out whoever hides where you're looking (a wrong guess costs a 30 s cooldown) |
| **T** | bear trap (4 per round) |
| **G** | poison gas in the vents |
| **R** | scream |
| **F** | lights out around you |

## Troubleshooting
- **No panel, or the mod doesn't load:** check `BepInEx\LogOutput.log` in your game folder for `Loading [NunMP ...]`.
- **"mod version X != host Y"** or **"different game build":** everyone must run the same mod version and the
  same itch build of the game.
- **Can't see the host's lobby:** you must be Steam friends, and both of you need Steam running.
  Or use the lobby code: the host clicks **Copy** and sends it to you.
- **Friends can't hear you / you're too quiet:**
  - Use **Test mic** in F2.
  - Keep **Mic: direct** with **Auto boost** on, or raise the manual boost.
  - Try another mic device.
  - **Voice stats** in F2 show packets and gaps for each player.
- **The mod is too loud somewhere / nun cue too loud:** the F2 panel has volume sliders.

## Uninstall
Delete these from your game folder:
- `BepInEx\`
- `winhttp.dll`
- `doorstop_config.ini`
- `.doorstop_version`
- `changelog.txt`

## Known limits
- No joining mid-game. If the host leaves, the session ends.
- Some puzzle cutscenes and scripted scares only play for the player who triggered them.
- The larger lobby sizes (more than 2–3 players) have had little real-world testing. Bug reports welcome! Open an [issue](https://github.com/Solendor-S/Nun-Massacre-Multiplayer/issues)
  or email **solendor.queries@gmail.com**.

## Building from source
Requires the .NET SDK (6 or newer) and a Nun Massacre install with BepInEx (run the installer once).

1. Copy `Steamworks.NET.dll`, `steam_api64.dll` and `Concentus.dll` from a release zip
   (`game/BepInEx/plugins/NunMP/`) into `src/lib/`.
2. `cd src` then `dotnet build -c Release -p:GameDir="C:\path\to\your\Nun Massacre"`.

   The build references the game's own assemblies from that folder and copies the plugin into its
   `BepInEx/plugins/NunMP/`.
3. `tools/package.sh` builds the release zip.

`tools/concentus/` has the recipe for rebuilding `Concentus.dll` from source. `tools/*.sh` are dev scripts
(bash), mostly two-instance loopback tests driven through a small in-game command bridge.

### How it works (short version)
- **Each machine runs a normal single-player game.** Remote players are puppets.
- **The host runs the nun's AI**, and it sees remote players through invisible stand-ins.
- **World changes are replicated** as the game's own events: doors, items, puzzles, lights.
- **Gameplay** goes host-relayed over Steam networking; **voice** goes peer to peer.

## Credits
- **Nun Massacre** and **Power Drill Massacre** by [Puppet Combo](https://puppetcombo.itch.io/). Please buy their games.
- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) and [HarmonyX](https://github.com/BepInEx/HarmonyX) (MIT): mod loader and patching.
- [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) (MIT) and the Steamworks API (Valve): lobbies and networking.
- [Concentus](https://github.com/lostromb/concentus) (BSD; license in `tools/concentus/LICENSE`): pure C# Opus codec.
- [UnityPy](https://github.com/K0lb3/UnityPy) (MIT): used by the optional PDM model export.

**AI disclosure:** this mod was designed with and directed by Solendor-S, and written by Claude (Anthropic) in
Claude Code. It was tested in loopback and in real Steam playtests with friends.

## License
[MIT](LICENSE) for the NunMP source code. Game files and third-party libraries keep their own licenses.
