# ADOFAI native macOS TUI installer

This project builds a native `osx-arm64` terminal installer for Unity Mod Manager on
the macOS build of **A Dance of Fire and Ice**. It does not run the old WinForms
installer, Mono, or Rosetta.

## Build

```sh
dotnet publish MacTuiInstaller/MacTuiInstaller.csproj -c Release -r osx-arm64 --self-contained true
```

Output:

```text
MacTuiInstaller/bin/Release/net10.0/osx-arm64/publish/adofai-umm
```

## Use

```sh
./adofai-umm --status
./adofai-umm --install --yes
./adofai-umm --game "/path/to/ADanceOfFireAndIce.app" --install
```

Without arguments it opens a small TUI menu.

The installer first looks for Unity Mod Manager DLLs beside itself, in the game
install, and in this repo's `lib` folder. If `UnityModManager.dll` is missing it
downloads the official Unity Mod Manager zip and caches the needed DLLs under
`~/.cache/adofai-umm-mac/payload`.

The ADOFAI hook target is:

```text
[UnityEngine.CoreModule.dll]UnityEngine.MonoBehaviour.cctor:Before
```

Every changed file gets a timestamped `.nativeumm_backup_yyyyMMdd_HHmmss` backup.
The original hooked assembly is also preserved as `UnityEngine.CoreModule.dll.original_`.
