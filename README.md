# StarVaders Ultrawide

An experimental BepInEx IL2CPP mod for StarVaders by NorthernDale.

## Features

- Height-based UI scaling on ultrawide displays.
- Adjustable encounter HUD and board sizes, with HUD panels near the screen edges.
- Mech and pilot selection layout corrections.
- Resolution recovery by toggling the fix off and on.
- Mod settings available from Options or F10. The pause screen has no mod button.

## Installation

Requires a Windows installation of StarVaders with BepInEx 6 IL2CPP installed.
Development used StarVaders 2.0.5, Unity 2020.3.49f1, and BepInEx 6 build 788.

1. Run the game once with BepInEx to generate its interop assemblies, then exit.
2. Build the mod using the instructions below.
3. Copy `bin/Release/net6.0/StarVadersUltrawide.dll` into the game's `BepInEx/plugins` folder.
4. Launch the game. Settings are saved in `BepInEx/config/local.starvaders.ultrawide.cfg`.

For the development BepInEx build, `UnityLogListening` in `BepInEx/config/BepInEx.cfg` was set to `false` to avoid a startup crash.

## Controls

| Key | Action |
| --- | --- |
| F6 / F7 | Decrease / increase HUD and board size |
| F8 | Toggle the ultrawide fix |
| F9 | Write canvas and camera diagnostics to the BepInEx log |
| F10 | Open or close mod settings |

The default HUD and board multiplier is 123%. Hotkeys can be changed in the config file with the game closed. If a resolution change leaves the mod out of sync, toggle F8 off and on with resolution forcing enabled.

## Building

Install the .NET 6 SDK. The project references assemblies from your own game installation; game and BepInEx binaries are not included.

```powershell
dotnet build -c Release -p:GameDir="D:/SteamLibrary/steamapps/common/StarVaders/"
```

Replace `GameDir` with your installation folder. The project defaults to the path shown above.

## Status

Work in progress. Mech/pilot selection and one empty-tile mouse alignment check were verified at 3440×1440 during development. Some campaign/shop menu backgrounds and clipped controls still need cleanup. The latest rack and menu changes need further visual verification.

The mod caches live UI references and avoids redundant transform writes to reduce CPU/UI overhead. Release compilation passes with no warnings or errors; frametime improvements have not been benchmarked.

The IL2CPP injector workaround in `Plugin.Load` is tied to the development BepInEx/Il2CppInterop version and may need adjustment with other versions.
