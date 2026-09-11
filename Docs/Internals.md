# Internals

[← README](../README.md)

## Patching notes

Patch attributes declare the Harmony targets, all of them in the `Finish` category. The
preloader installs that category before `Program.Main` can be JIT-compiled. Keep new patches
there unless you know their target runs after plugin initialization.

Krafs.Publicizer gives compile-time access to non-public game APIs. Its `<Publicize>` entries in
`ClientPlugin/ClientPlugin.csproj` have to match the `IgnoresAccessChecksTo` declarations in
`ClientPlugin/Tools/GameAssembliesToPublicize.cs`.

`ClientPlugin/GlobalUsings.cs` carries the usings the sources were written against. Pulsar's
Roslyn compiler does not run MSBuild, so it never applies `ImplicitUsings`.

Pulsar's plugin compiler picks up `VRage.Library.Generator` transitively, and that assembly
duplicates several `VRage.Library` types. Code touching any of those conflicting types has to go
through reflection.

Harmony 2.4.2 cannot patch methods containing exception filters, and MonoMod cannot rewrite open
generic definitions. The affected renderer patches target callers or constructed generic
methods instead.

Pulsar's build cache sits under `~/.config/Pulsar/Modern/DevFolder/linux-compat-*`. Delete the
matching cache directory to force a source rebuild and a fresh asset deployment. Compile errors
land in `~/.config/Pulsar/Modern/info.log`, game logs in `~/.config/SpaceEngineers2/Temp/Logs/`.

## Repository layout

- `ClientPlugin/Patches/`: Harmony patches, grouped by compatibility function into `NullSafety`,
  `PathHandling`, `PlatformGuards`, `Rendering`, `SystemAbstraction` and `UIDisplay`
- `ClientPlugin/Preloading/`: Cecil rewrites applied before game assemblies load
- `ClientPlugin/Platform/`: SDL windowing, input and threading, Linux engine components, the data
  folder, HTTP, the splash screen, the startup dependency check, and assembly and native library
  resolution
- `ClientPlugin/Tools/`: publicizer declarations, transpiler helpers and preloader helpers
- `ClientPlugin/Preloader.cs`: ReadyToRun rewriting and early `Finish` category installation
- `ClientPlugin/ReadyToRun.cs`: shipped ReadyToRun assembly list
- `ClientPlugin/Plugin.cs`: the `IPlugin` Pulsar loads. All the compatibility work happens in
  the preloader, so this type only announces the plugin
- `ClientPlugin/ClientPlugin.xml`: Pulsar and PluginHub metadata, including native assets
- `Checks/InstallSmoke/`: patch installation and Cecil rewrite smoke check
