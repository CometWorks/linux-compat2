# LinuxCompat for Space Engineers 2

Client plugin that patches the original Space Engineers 2 binaries to run **natively on
Linux**, loaded by [Pulsar](https://github.com/SpaceGT/Pulsar)'s Modern executable. No
recompiled game code is involved: every change is applied at run time with Harmony (plus a
single Cecil preloader rewrite of `VRage.Steam`), and the native Windows libraries are
served by prebuilt Linux wrappers and translation layers (DXVK, vkd3d-proton, SDL3, FMOD).

The patch set is the runtime migration of the recompiled-source prototype in
`dotnet-game2-local`; that repository's `LinuxCompat/PATCHES.md` is the authoritative ledger
describing each patch, its anchors, and its verification history.

## Status

The game starts from the original binaries through Pulsar Modern on Linux, shows the SDL
splash, reaches and renders the main menu (native Vulkan via vkd3d-proton/DXVK), downloads
news banners, initializes Steam through Pulsar's Linux Steamworks pair, and exits cleanly
via "Exit to Linux".

## Prerequisites

- Space Engineers 2 installed via Steam (native Linux Steam client)
- Pulsar deployed with its `Modern.bin` Linux executable (this repo assumes `~/.config/Pulsar`)
- .NET 10 runtime (used by Pulsar Modern)
- Vulkan-capable GPU driver
- Dependency archives from the sibling repositories (see `LinuxCompat.xml` assets):
  - `linux-dependencies/dist/se2-dependencies.tar.gz` (SDL3, DXVK, vkd3d-proton, FMOD)
  - `linux-dependencies/dist/steam-dependencies.tar.gz` (Steamworks pair)
  - `linux-native-wrappers/dist/se2-native-wrappers.tar.gz` (VRage native wrappers)
  - the DXC shader compiler trio, currently from `dotnet-game2-local/RenderingLibs`
  - Windows Desktop runtime assemblies staged by `prepare-windesktop-stubs.sh`

## Setup

1. Register the dev folder in `~/.config/Pulsar/Modern/Sources/sources.xml`:

   ```xml
   <LocalPlugin>
     <Name>linux-compat</Name>
     <Folder>/path/to/linux-compat2</Folder>
     <File>LinuxCompat.xml</File>
     <Enabled>true</Enabled>
   </LocalPlugin>
   ```

2. Enable the plugin in `~/.config/Pulsar/Modern/Profiles/Current.xml`:

   ```xml
   <DevFolder>
     <LocalFolderConfig>
       <Id>linux-compat</Id>
       <DebugBuild>true</DebugBuild>
     </LocalFolderConfig>
   </DevFolder>
   ```

3. Stage the Windows Desktop assemblies once:

   ```bash
   ./prepare-windesktop-stubs.sh
   ```

## Launch

```bash
DOTNET_ReadyToRun=0 ~/.config/Pulsar/Modern.bin -noUpdate -sources -noPrompt
```

`DOTNET_ReadyToRun=0` is **required**: the shipped game assemblies are win-x64 ReadyToRun
images, which the Linux runtime refuses to load unless the precompiled code is ignored.

## Development notes

- `Checks/InstallSmoke` installs every Harmony patch against the shipped binaries (all
  transpiler anchors execute at patch time) and force-JITs the Cecil-rewritten
  `VRage.Steam` methods, without starting the game:

  ```bash
  dotnet build Checks/InstallSmoke/InstallSmoke.csproj
  COMPlus_ReadyToRun=0 SE2_NATIVE_DIR=/dir/with/all/so/files \
    dotnet Checks/InstallSmoke/bin/Debug/net10.0/InstallSmoke.dll
  ```

- Pulsar's plugin compiler transitively references `VRage.Library.Generator`, which
  duplicates several `VRage.Library` types (`HashSetReader`,
  `MetadataDependenciesAttribute`, `IndexMetadataAttribute`,
  `ModuleIndexedAttributesAttribute`). Plugin sources must never name these types; the
  patches touching them work through reflection instead.
- Harmony 2.4.2 cannot patch methods containing exception filters and MonoMod cannot
  rewrite open generic definitions; the affected patches target callers or constructed
  instantiations (see comments in `Patches/Install/Render12Patches.cs`).
- Plugin build cache: `~/.config/Pulsar/Modern/DevFolder/linux-compat-*/`; delete it to
  force a rebuild and asset redeploy. Compile errors appear in
  `~/.config/Pulsar/Modern/info.log`; game logs in `~/.local/share/SpaceEngineers2/Temp/Logs/`.

## Repository layout

- `ClientPlugin/Patches/` — ported LinuxCompat patches (Harmony patch bodies and helpers)
- `ClientPlugin/Patches/Install/` — installers that bind them to the shipped binaries,
  including the transpilers with their IL anchors, and the `VRage.Steam` Cecil prepatch
- `ClientPlugin/Platform/` — the Linux platform implementation (SDL windowing/input,
  splash, HTTP, engine components, native library resolver)
- `ClientPlugin/Preloader.cs` — Pulsar preloader hooks; `Finish()` installs everything
  in-process before the game's `Main` runs
- `LinuxCompat.xml` — Pulsar registration including the native dependency assets
