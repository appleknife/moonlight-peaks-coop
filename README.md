# Moonlight Peaks Co-op

Early host/guest co-op mod for **Moonlight Peaks** (Steam app 2209900). Player 2 joins
player 1 over direct IP, is visible and animated, follows room changes, shares the day
clock, follows cutscenes, and keeps their own player data and NPC relationships. World
changes are copied between clients but still reload the room.

- Tested game build: `25725760` (Unity 6000.3.24f1, Mono), exe SHA-256 `04BE5CFFF9909AF892968276D085E9C11B93BA82886749516A521250113511DB`.
- Co-op build: 0.1.4 (alpha). Back up saves before trying it.
- The mod is BepInEx 5.4.23.2 (SHA-256 `F752CE4E838F4C305B9DA1404B6745F2CFF23B8BFD494F79F0C84D0A01F59B46`).

## Install

1. Close the game.
2. Right-click `install.ps1` -> Run with PowerShell. It finds the Steam install from the
   Steam library file and installs BepInEx plus the plugin there. Original `winhttp.dll`,
   `doorstop_config.ini` and `.doorstop_version` are backed up next to the game.
3. `install.ps1 -Copy` installs into a separate copy under
   `%LOCALAPPDATA%\MoonlightPeaksCoop\game` instead of touching the Steam install.
4. Start the game with `Play Moonlight Peaks Co-op.bat`.

Uninstall with `uninstall.ps1`. It removes the plugin and restores the backed-up files.

Saves are kept apart per profile under `BepInEx/coop-saves/<profile>`, so the mod does not
write to the normal save folder. The first mod launch copies your existing saves into the
`default` profile, so they still appear in the load menu. Launching from Steam works too
and loads the mod; use `uninstall.ps1` for a vanilla install.

## Play

Easiest without router changes: install [ZeroTier](https://www.zerotier.com/download/) on
both PCs, create a free network, have your friend join it, accept the member in ZeroTier
Central, then use the host's ZeroTier address (starts with `10.` or `172.`) as the join
address. Allow Moonlight Peaks through Windows Firewall on Private networks when prompted.

With port forwarding instead: forward UDP `47110` and TCP `47120` to the host PC and use
its public IP.

Host:
1. Load a save and stand in the world.
2. Press `F8`, click **Host**.
3. Allow UDP `47110` and TCP `47120` through the firewall / router and give your friend
   your public IP.

Friend:
1. Start the game, stay on the main menu.
2. Press `F8`, type the host's IP in **Address**, click **Join**. The world save is
   downloaded from the host and loaded automatically.
3. Press `F8` to hide the overlay.

`F3` disconnects. `F6` loads the newest local save. `F9` spawns/removes a test ghost.

## What works (checked on two local clients)

- Connection, player ghosts with position/rotation and walk animation.
- Room changes load on the other client in both directions (host or guest).
- Day/time and Director graph nodes are mirrored, so host and guest cutscenes and day starts run on both screens.
- Guest-triggered cutscenes resolve to the guest's body on the host.
- Held tools sync both ways, including unequip and after a room reload.
- Guest loads the host's save over TCP and gets a fresh player record and fresh NPC
  relationship list, so relationships are per player.
- Room world state (planted crops, harvest status, water, pickups, shopping) is pushed between clients and the room is refreshed.

## Known issues

- A room change during a cutscene waits for the cutscene to get past its wake-up, so it
  can take ~20-30 s; the cutscene's own room change may win over a requested warp.
- Coins are per player. This is alpha: expect bugs.
- World refresh reloads the room on the receiving client, so it briefly repositions; rapid farming can rubber-band.
- No lobby or NAT punch-through; direct IP and a forwarded port are required.
- Only this game build was checked.

## Build

Needs Windows, PowerShell and a legitimate game install. The pinned Roslyn compiler is in
`tools\roslyn`; BepInEx is in `dist`.

1. `.\install.ps1 -Copy` makes a test copy with BepInEx under
   `%LOCALAPPDATA%\MoonlightPeaksCoop\game`.
2. `.\build.ps1 -GameDir "$env:LOCALAPPDATA\MoonlightPeaksCoop\game"` (or point `-GameDir`
   at any copy with `BepInEx\core`).
3. Output is `out\MoonlightPeaksCoop.dll`. The built plugin is also kept in `dist` for
   players who only want to install.

`src` compiles against the game's `Vampire.Runtime.dll`; no game files are committed.
