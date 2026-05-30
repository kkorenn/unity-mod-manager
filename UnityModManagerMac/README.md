# Unity Mod Manager — native macOS app

A native SwiftUI app that installs Unity Mod Manager into the macOS build of
**A Dance of Fire and Ice**. It replaces the `MacTuiInstaller` terminal tool with
a GUI, while reusing that tool's proven assembly-patching core.

## How the C# core is fused in (no separate binary, no child process)

The installer's heart is IL patching of `UnityEngine.CoreModule.dll` via
[dnlib](https://github.com/0xd4d/dnlib) — there is no native Swift equivalent.
Instead of shelling out to a bundled executable, the C# logic is compiled with
**.NET NativeAOT** into a static library (`libNativeUmm.a`) and linked directly
into the app binary. The C# runs **in-process**, called through two C entry points:

```c
char *umm_run(const char *requestJson);   // one JSON request -> one JSON response
void  umm_free(char *response);
```

`InstallerService.swift` builds a request, calls `umm_run`, and decodes the JSON.
The Release build is a single self-contained ~30 MB Mach-O — the .NET runtime,
dnlib, OpenSSL and Brotli are all statically linked; there are no Homebrew or
other dynamic dependencies at runtime.

## UI

Modeled on the classic Windows UnityModManager Installer — three tabs plus a
bottom status bar:

- **Install** — Install / Reinstall, Uninstall, Restore original files (patches
  `UnityEngine.CoreModule.dll`); current vs. in-game version; a Home Page button;
  and a **Recommended mods** list with per-mod toggles + an "Install Mods" button
  that downloads and installs the ticked mods. The catalog is
  [RecommendedMods.json](UnityModManagerMac/RecommendedMods.json) — add entries
  there (`id`, `name`, `description`, `url` pointing at the mod's `.zip`).
- **Mods** — table of installed mods (name / version / manager / status), an
  "Install Mod" button, and a drop zone for installing mod `.zip` files.
- **Log** — tail of `UnityModManager/Log.txt`.

```
ContentView.swift ──> InstallerService.swift ──(C)──> libNativeUmm.a (NativeAOT)
                                                          ├─ Exports.cs   (umm_run/umm_free, JSON)
                                                          ├─ Installer.cs (dnlib patching, verbatim)
                                                          ├─ GameLayout.cs (Steam auto-detect)
                                                          └─ Support.cs   (hook spec, log buffer)
```

## Build

Prerequisites (build-time only — not shipped):

```sh
brew install dotnet openssl@3 brotli
```

1. Build the native core and wire it into the Xcode project:

   ```sh
   ./build-native.sh
   ```

   This publishes the NativeAOT static lib, stages the UMM payload DLLs into
   `Payload/` (bundled at `Contents/Resources/Payload`), resolves the
   version-specific .NET runtime-pack path + Homebrew lib paths, and regenerates
   `NativeUmm.xcconfig` with the link flags. **Re-run it whenever the C# changes
   or the .NET runtime pack version changes.**

2. Build the app:

   ```sh
   xcodebuild -project UnityModManagerMac.xcodeproj -scheme UnityModManagerMac -configuration Release build
   ```

   …or just open `UnityModManagerMac.xcodeproj` and press Run.

## Notes

- **arm64 only.** The Homebrew static OpenSSL/Brotli libs are arm64. A universal
  build would need x86_64 builds of those plus per-arch `lipo` of every runtime
  archive.
- **App Sandbox is disabled** (`ENABLE_APP_SANDBOX = NO`): the installer writes
  into the Steam game folder, outside any sandbox container. The app is therefore
  not App Store eligible.
- The installer's behavior (hook target, payload resolution, timestamped backups,
  `UnityEngine.CoreModule.dll.original_`) is unchanged from `MacTuiInstaller`.
