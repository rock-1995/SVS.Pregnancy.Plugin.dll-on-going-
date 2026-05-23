# SVS Pregnancy

A BepInEx IL2CPP plugin that adds a lightweight pregnancy system to
Summer Vacation Scramble.

The project focuses on keeping the feature set self-contained for SVS: it
does not require external character-body plugins, and it includes its own
runtime belly morphing and clothing deformation support.

## Features

- Pregnancy can occur during player-controlled H scenes.
- Pregnancy chance is affected by configurable ovulation rates for safe,
  normal, and dangerous days.
- Fertility calculations also consider character state, relationship context,
  chastity, resistance, weakness/lewdness state, and whether climax timing is
  synchronized.
- Optional futanari insemination support. Only biologically female characters
  can become pregnant.
- Pregnancy progression speed is configurable. The default is a 40-week
  pregnancy, with faster progression options available.
- Birth timing varies around term. Late pregnancy can result in healthy birth;
  earlier outcomes may produce premature birth or miscarriage.
- Pregnancy, birth, and miscarriage can affect character emotions and
  relationship values.
- Pregnancy data is saved beside the game save file and restored on load.
- Runtime belly morphing for pregnant characters.
- Clothing deformation support, including mesh-readable loading, surface-based
  clothing morphing, layered-clothing preservation, and reduced belly clipping.
- In-game debug UI, default key `F8`, for inspecting pregnancy state and tuning
  belly deformation parameters.
- Diagnostic mesh spy tools are available through BepInEx Configuration
  Manager and are disabled by default.

## Install

Copy the built plugin to:

```text
SamabakeScramble\BepInEx\plugins\SVS_plugins\SVS_Pregnancy.dll
```

The plugin requires a working BepInEx IL2CPP setup for Summer Vacation
Scramble.

## Configuration

After the first launch, settings are written to:

```text
BepInEx\config\SVS.SVSPregnancy.cfg
```

Most gameplay settings can also be edited through BepInEx Configuration
Manager:

- `General > Enable`
- `General > Log Enable`
- `General > Futanaris Can Inseminate`
- `General > Pregnancy progression speed`
- `General > Ovulation Rate in Safe Days`
- `General > Ovulation Rate in Normal Days`
- `General > Ovulation Rate in Dangerous Days`
- `Debug > Debug UI Key`
- `Debug > Enable Spy`

`Enable Spy` is intended for diagnostics only. The clothing mesh loader remains
active even when spy logging is disabled.

## Build

This repository does not include proprietary game assemblies. Build references
are resolved from a local game installation.

Option 1: pass the game folder on the command line:

```powershell
dotnet build .\SVS_Pregnancy.sln -c Release -p:SVSGameDir="D:\Games\Summer Vacation Scramble v1.1.5P1\SamabakeScramble"
```

Option 2: copy `Directory.Build.props.example` to `Directory.Build.props`,
edit `SVSGameDir`, then build:

```powershell
dotnet build .\SVS_Pregnancy.sln -c Release
```

The built plugin is written to:

```text
bin\Release\net6.0\SVS_Pregnancy.dll
```

## Development Notes

- `Directory.Build.props` is ignored by git so each developer can keep their
  own local game path.
- Build output and game assemblies are intentionally not committed.
- The debug UI is meant for tuning and inspection. Normal gameplay does not
  require it.

## Credits

Thanks to the authors of Pregnancy Plus for the original work and ideas this
project builds on.

Thanks also to the author of "monkey version" pregnancy plugin, which provided
the starting point for it.https://zodgame.xyz/forum.php?mod=viewthread&tid=471375&extra=
