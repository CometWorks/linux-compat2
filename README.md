# Linux compatibility for Space Engineers 2

LinuxCompat patches the original Space Engineers 2 binaries so the game can run natively on
Linux through [Pulsar](https://github.com/SpaceGT/Pulsar)'s Modern executable. Harmony and
Pulsar's Cecil preloader apply the changes at run time. The plugin does not ship recompiled game
assemblies.

The native Windows libraries are supplied by Linux wrappers and translation layers, including
DXVK, vkd3d-proton, SDL3, FMOD, and AMD FidelityFX (the FSR 3.1 upscaler, built for Linux).

## Status

The game starts through Pulsar Modern on Linux, reaches the main menu, renders through Vulkan,
upscales through FSR 3.1 when the graphics Quality setting selects it, initializes Steam,
downloads news banners, and exits cleanly through "Exit to Linux".

## Prerequisites

- Space Engineers 2 installed by the native Linux Steam client
- Pulsar with its `Modern.bin` Linux executable, normally under `~/.config/Pulsar`
- .NET 10 runtime
- A Vulkan-capable graphics driver
- The dependency archives listed in `ClientPlugin/ClientPlugin.xml`
  (paths are relative to the repository root)

## Development setup

The build locates the standard Steam and Pulsar directories automatically. For a different
installation, create the git-ignored `Directory.Build.props.user` file in the repository root:

```xml
<Project>
    <PropertyGroup>
        <Game2>/path/to/SpaceEngineers2/Game2</Game2>
        <Pulsar>/path/to/Pulsar</Pulsar>
    </PropertyGroup>
</Project>
```

Register the repository as a Pulsar development source in
`~/.config/Pulsar/Modern/Sources/sources.xml`:

```xml
<LocalPlugin>
  <Name>linux-compat</Name>
  <Folder>/path/to/linux-compat2</Folder>
  <File>ClientPlugin/ClientPlugin.xml</File>
  <Enabled>true</Enabled>
</LocalPlugin>
```

Existing registrations that name `LinuxCompat.xml` must change their `<File>` value to
`ClientPlugin/ClientPlugin.xml`.

Enable it in `~/.config/Pulsar/Modern/Profiles/Current.xml`:

```xml
<DevFolder>
  <LocalFolderConfig>
    <Id>linux-compat</Id>
    <DebugBuild>true</DebugBuild>
  </LocalFolderConfig>
</DevFolder>
```

Build the solution with:

```bash
dotnet build LinuxCompat.sln
```

The same command works on Windows and Linux. Use `dotnet clean LinuxCompat.sln` to remove build
output; the repository does not use platform-specific build scripts.

The client project deploys `plugin.dll` and `plugin.xml` to
`~/.config/Pulsar/Modern/Local/linux-compat` when the Pulsar path is available.

## Formatting

Install CSharpier once, then format the repository before each commit:

```bash
dotnet tool install -g csharpier
csharpier format .
```

Use `csharpier check .` to verify formatting without changing files. `.csharpierignore` limits
CSharpier to C# source and excludes build output.

## Verification

`Checks/InstallSmoke` installs the full `Finish` Harmony category against the shipped binaries.
This runs every transpiler and checks its IL anchors without starting the game. It also applies
the `VRage.Steam` Cecil rewrite and prepares the rewritten methods.

```bash
dotnet build Checks/InstallSmoke/InstallSmoke.csproj -p:Game2=/path/to/SpaceEngineers2/Game2
COMPlus_ReadyToRun=0 \
SE2_NATIVE_DIR=/dir/with/all/so/files \
PULSAR_LIBRARIES=/path/to/Pulsar/Libraries/Modern \
  dotnet Checks/InstallSmoke/bin/Debug/net10.0/InstallSmoke.dll \
  /path/to/SpaceEngineers2/Game2
```

## Launch

```bash
~/.config/Pulsar/Modern.bin -noUpdate -sources -noPrompt \
  -game2 /path/to/SpaceEngineers2/Game2
```

Before `Program.Main` runs, the preloader converts the shipped ReadyToRun assemblies listed in
`ClientPlugin/ReadyToRun.cs` to IL-only images in Pulsar's preloader cache. Set
`DOTNET_ReadyToRun=0` or `COMPlus_ReadyToRun=0` to skip those targets. The `VRage.Steam`
compatibility rewrite still runs.

## Loading a world with many workshop mods

Space Engineers 2 re-downloads every mod of a world on every load, even when Steam has the
content installed and current: `SteamUGCServiceComponent.GetModDataFilesystemAsync` resolves each
mod with `DownloadItem(id, force: true)`. Steam serves a handful of items that way and then
refuses the rest, and a single refusal aborts the whole load — so a world with more than a few
mods is not reliably loadable. This is the game's behaviour, not a Linux one.

Setting `SE2_DISABLE_FORCED_REDOWNLOAD=1` makes the preloader neutralise that `force` argument,
which reinstates `DownloadItem`'s own guard: content that is installed and not flagged for update
is used as it is, and anything missing or out of date is still downloaded before it is mounted.

```bash
SE2_DISABLE_FORCED_REDOWNLOAD=1 ~/.config/Pulsar/Modern.bin -noUpdate -sources -noPrompt \
  -game2 /path/to/SpaceEngineers2/Game2
```

It is opt-in because it changes how the game talks to Steam rather than how it runs on Linux. A
run with it applied logs one line saying so.

## Patching notes

Harmony targets are declared with patch attributes and the `Finish` category. The preloader
installs that category before `Program.Main` can be JIT-compiled. Keep patches in this category
unless their target is known to run after plugin initialization.

Krafs.Publicizer handles compile-time access to non-public game APIs. The `<Publicize>` entries
in `ClientPlugin/ClientPlugin.csproj` must match the `IgnoresAccessChecksTo` declarations in
`ClientPlugin/Tools/GameAssembliesToPublicize.cs`.

Pulsar's plugin compiler transitively references `VRage.Library.Generator`, which duplicates
several `VRage.Library` types. Code that touches those conflicting types must continue to use
reflection.

Harmony 2.4.2 cannot patch methods containing exception filters, and MonoMod cannot rewrite open
generic definitions. The affected renderer patches target callers or constructed generic
methods instead.

Pulsar's build cache is under `~/.config/Pulsar/Modern/DevFolder/linux-compat-*`. Remove the
matching cache directory to force a source rebuild and asset deployment. Compile errors are in
`~/.config/Pulsar/Modern/info.log`; game logs are in
`~/.config/SpaceEngineers2/Temp/Logs/`.

## Repository layout

- `ClientPlugin/Patches/`: Harmony patches grouped by compatibility function
- `ClientPlugin/Preloading/`: Cecil rewrites applied before game assemblies load
- `ClientPlugin/Platform/`: SDL windowing and input, Linux engine components, HTTP, and native library loading
- `ClientPlugin/Settings/`: Pulsar configuration screen support
- `ClientPlugin/Preloader.cs`: ReadyToRun rewriting and early `Finish` category installation
- `ClientPlugin/ReadyToRun.cs`: shipped ReadyToRun assembly list
- `ClientPlugin/ClientPlugin.xml`: Pulsar and PluginHub metadata, including native assets
- `Checks/InstallSmoke/`: patch installation and Cecil rewrite smoke check

## Bug reports

Open an issue with the game and Pulsar logs, or start a support thread on the
[Pulsar Discord](https://discord.gg/z8ZczP2YZY).

## Legal

Space Engineers 2 is a trademark of Keen Software House s.r.o.
