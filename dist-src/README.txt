NUN MASSACRE MULTIPLAYER (NunMP) 0.6.4 - co-op, 2-8 players vs the nun
=======================================================================

NEEDS
- Nun Massacre Definitive Edition, itch.io build 2022.06.08 (everyone must have the SAME build).
- Steam running and logged in. You'll show as "playing Spacewar" - that's normal (we use Steam's
  free test app for invites + connection, no port forwarding needed).
- Optional: Power Drill Massacre 0.04 (free demo on itch) + Python, so teammates look like PDM's
  survivor instead of a placeholder. Each player extracts it from their OWN copy.

INSTALL
1. Unzip anywhere. Double-click install.bat.
   (If you run install.ps1 by hand, use: powershell -ExecutionPolicy Bypass -File .\install.ps1)
2. Pick your Nun Massacre folder. Answer the PDM question.
   (To uninstall: delete BepInEx, winhttp.dll, doorstop_config.ini, .doorstop_version, changelog.txt
   from the game folder.)

PLAY
1. Launch Nun Massacre. On the main menu the multiplayer panel opens (F2 toggles it).
2. Host: choose the gamemode, click "Host game", then wait for friends.
   Friends: click "Join <host name>" (or paste the host's lobby code and click Join).
3. Host sets Play options as usual, then clicks START GAME. Everyone is taken in together.

RULES (Co-op Survival)
- The nun hunts whoever she sees/hears; the closest player otherwise.
- Shared lives (3, or 1 on Extreme). Caught with lives left = you respawn. Caught with none left =
  you spectate a teammate (click to switch). Everyone caught = game over.
- Anyone escapes = everyone gets the ending.
- Doors, locks, puzzles, items, lights, cabinets and glass are shared. Inventories are your own.
- One lighter per survivor, all in the lighter's drawer; falling in the barbed-wire pit is a normal death.

VOICE (proximity)
- Hold V to talk (or switch to open mic in the F2 panel). The F2 panel has: voice volume (up to 300%),
  "Mic: direct" (default, louder) with Auto boost (adapts to quiet mics like laptop mics) or a manual
  boost up to 40x, mic device choice, "Mic: Steam" as a fallback, and "Test mic" with a level meter.
- Voices come from the player, get muffled through walls and echo like the room they are in.
- Voice is compressed (Opus) and goes straight from player to player (through Steam's relays, no IPs
  shown), only to people close enough to hear you or on the radio - the host doesn't carry it.
- If the host leaves "The nun can hear you talk" on, talking is noise: whisper near her and she may come.
- Caught players (spectating) can only be heard by other spectators.
- WALKIE-TALKIES: one per survivor waits in the drawer under the lighter. Pick one up, hold B to talk to everyone
  else carrying one (you still talk out loud too). Careful: a radio receiving chatter crackles, and the
  nun can hear it.
- When someone is caught, everyone sees them collapse ("<name> WAS CAUGHT").
- The radar shows teammates too: steady bright dots (the nun is the blinking one). No name tags over heads.

PLAY AS NUN (gamemode)
- Host picks "Play as Nun" and who the nun is (or Random). The AI nun is switched off; one player IS her.
- Survivors: same rules as co-op (lives, spectate, escape = win). Her music/sting plays when you see her close.
- The nun (first person, dark vision):
    WASD move   Shift sprint (stamina)   Ctrl/C crouch-sneak (fits vents, no screen static)
    Stand still ~2.5 s = lurking: your view narrows and survivors near you get no screen static
    Shift while crouched = crouch-run: a bit faster, burns stamina twice as fast, gives you away
    Q / E peek left / right (her body leans too)
    Left click  swing (40 damage, cooldown shown under the crosshair, HIT flashes when it lands)
    Right click drag a survivor out of the bed/cabinet you're looking at (wrong spot = 30 s cooldown)
    T  bear trap (4 per round)           G  poison gas in the vents (15 s)
    R  scream (static + stamina drain near you)
    F  lights out around you (10 s, survivors' lighters sputter and almost die)
    You hear the jumpscare sting and get told when a survivor spots you.
    Your voice reaches survivors deep, distorted and echoing.
- The nun player picks her look in the lobby: Nun, Clown, Backrooms or Driller.
    ( ! ) noise pings appear only when no survivor has seen you for 90 s
    Shot by a survivor's revolver: you stagger, and for 25 s you can't swing, use powers or get pings - run.
- Nun wins when every survivor is caught; survivors win if anyone escapes.

KNOWN LIMITS
- No joining mid-game. If the host leaves, the session ends.
- Some puzzle cutscenes / scripted scares only play for the player who triggered them.

Made with BepInEx (LGPL) and Steamworks.NET (MIT). Not affiliated with Puppet Combo.
