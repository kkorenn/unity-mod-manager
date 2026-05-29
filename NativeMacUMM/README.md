# NativeMacUMM

Native macOS (Apple Silicon) Unity Mod Manager-style app for **A Dance of Fire and Ice** only.

## Features

- Native AppKit UI (no Mono runtime required).
- ADOFAI-only install/repair flow.
- Installs bundled UMM payload files (including patched `UnityEngine.CoreModule.dll`).
- Optional Rosetta wrapper install for `ADanceOfFireAndIce` executable.
- Optional Mac QOL mod install (ADOFAI only).
- Drag-and-drop `.zip` mod import into `Mods` folder.
- Mod list table (Name / Version / Id / Folder).
- Build scripts for `.app` and `.dmg`.

## Prepare Payload

Before building the app, extract payload from a known-working ADOFAI+UMM install:

```bash
cd NativeMacUMM
./scripts/prepare_payload_from_game.sh
```

Default game path used:

`~/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice`

You can also pass a custom game folder path.

## Build `.app`

```bash
cd NativeMacUMM
./scripts/build_app.sh
```

Output:

`NativeMacUMM/dist/Unity Mod Manager ADOFAI.app`

## Build `.dmg`

```bash
cd NativeMacUMM
./scripts/build_dmg.sh
```

Output:

`NativeMacUMM/dist/Unity Mod Manager ADOFAI.dmg`
