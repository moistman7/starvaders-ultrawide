# StarVaders Ultrawide

An experimental BepInEx IL2CPP mod for StarVaders by NorthernDale.

## Features

- Height-based UI scaling on ultrawide displays.
- Adjustable encounter HUD and board sizes, with HUD panels near the screen edges.
- Refresh enlarged board/HUD transforms before mouse hover, click, release, and cancel handling so hit testing follows the visible layout.
- Mech and pilot selection layout corrections.
- Resolution recovery by toggling the fix off and on.
- Mod settings available from the pause screen, Options, or F10. On the pause screen the entry is a copy of the game's own bottom-row button, centered between Show Guide and Abandon Run.
- Optional centered 16:9 HUD frame. Side panels grow as one piece from their top outer corner and stay clear of the reroll and End Turn buttons.
- The bottom rack keeps its native art: the center block stays under the reroll and End Turn buttons, and the end blocks under the deck and discard piles sit at the screen edges.

## Installation

Requires a Windows installation of StarVaders with BepInEx 6 IL2CPP installed.
Development used StarVaders 2.0.5, Unity 2020.3.49f1, and BepInEx 6 build 788.

1. Run the game once with BepInEx to generate its interop assemblies, then exit.
2. Build the mod using the instructions below.
3. Copy `bin/Release/net6.0/StarVadersUltrawide.dll` into the game's `BepInEx/plugins` folder.
4. Launch the game. Settings are saved in `BepInEx/config/local.starvaders.ultrawide.cfg`.

For the development BepInEx build, `UnityLogListening` in `BepInEx/config/BepInEx.cfg` was set to `false` to avoid a startup crash.

## Controls

The mod panel's **HUD FRAME** button switches between **FULL WIDTH** and **CENTERED 16:9**. Centered mode keeps encounter HUD panels in a height-based 16:9 area without changing the ultrawide view or board size. It also works with HUD size at 100%. The setting saves as `Interface/CenterHud16By9` (default false). Reset to 100% restores 100% HUD and board, without changing the HUD frame.

| Key | Action |
| --- | --- |
| F6 / F7 | Decrease / increase HUD and board size |
| F8 | Toggle the ultrawide fix |
| F9 | Write canvas and camera diagnostics to the BepInEx log |
| F10 | Open or close mod settings |

HUD and board sizes default to 100%. Hotkeys can be changed in the config file with the game closed. If a resolution change leaves the mod out of sync, toggle F8 off and on with resolution forcing enabled.

## Building

Install the .NET 6 SDK. The project references assemblies from your own game installation; game and BepInEx binaries are not included.

```powershell
dotnet build -c Release -p:GameDir="D:/SteamLibrary/steamapps/common/StarVaders/"
```

Replace `GameDir` with your installation folder. The project defaults to the path shown above.

## Status

Version 0.1.2 adds the centered 16:9 HUD frame (side panels move through invisible holders, so the game's panel animations are never overwritten), grouped side-panel scaling that keeps clear of the bottom buttons, native rack art at ultrawide, the pause-screen mod entry, 100% default sizes, and mod menu click fixes. Opt-in runtime checks at 3440x1440 pass at 100%, 123% and 135% HUD size; layouts were checked visually in an encounter and on the pause screen.

Version 0.1.1 adds the DiceVaders pointer-sizing fix adapted to StarVaders' persistent enlargement. Input refreshes reuse tracked native baselines, preserving animation changes and avoiding compounded scaling. Release build verified; live gameplay checks of this version remain pending.

Work in progress. Mech/pilot selection and one empty-tile mouse alignment check were verified at 3440×1440 during development. Some campaign/shop menu backgrounds and clipped controls still need cleanup.

The mod caches live UI references and avoids redundant transform writes. Measured mod CPU time in an encounter is about 0.2-0.26 ms per frame (3440x1440, centered, 123%). Set `STARVADERS_UW_PROFILE=1` to log frame timing every 10 seconds.

The IL2CPP injector workaround in `Plugin.Load` is tied to the development BepInEx/Il2CppInterop version and may need adjustment with other versions.
