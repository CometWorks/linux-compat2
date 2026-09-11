# Internals

[← README](../README.md)

## Patching notes

Harmony targets are declared with patch attributes and the `Finish` category. The preloader
installs that category before `Program.Main` can be JIT-compiled. Keep patches in this category
unless their target is known to run after plugin initialization.

Krafs.Publicizer handles compile-time access to non-public game APIs. The `<Publicize>` entries
in `ClientPlugin/ClientPlugin.csproj` must match the `IgnoresAccessChecksTo` declarations in
`ClientPlugin/Tools/GameAssembliesToPublicize.cs`.

`ClientPlugin/GlobalUsings.cs` carries the usings the sources were written against, because
Pulsar's Roslyn compiler does not run MSBuild and so never applies `ImplicitUsings`.

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

- `ClientPlugin/Patches/`: Harmony patches, grouped by compatibility function into `NullSafety`,
  `PathHandling`, `PlatformGuards`, `Rendering`, `SystemAbstraction` and `UIDisplay`
- `ClientPlugin/Preloading/`: Cecil rewrites applied before game assemblies load
- `ClientPlugin/Platform/`: SDL windowing, input and threading, Linux engine components, the data
  folder, HTTP, the splash screen, the startup dependency check, and assembly and native library
  resolution
- `ClientPlugin/Tools/`: publicizer declarations, transpiler helpers and preloader helpers
- `ClientPlugin/Preloader.cs`: ReadyToRun rewriting and early `Finish` category installation
- `ClientPlugin/ReadyToRun.cs`: shipped ReadyToRun assembly list
- `ClientPlugin/Plugin.cs`: the `IPlugin` Pulsar loads; the compatibility work happens in the
  preloader, so this type only announces the plugin
- `ClientPlugin/ClientPlugin.xml`: Pulsar and PluginHub metadata, including native assets
- `Checks/InstallSmoke/`: patch installation and Cecil rewrite smoke check
