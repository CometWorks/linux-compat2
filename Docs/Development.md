# Development

[← README](../README.md)

## Setup

The build finds the standard Steam and Pulsar directories on its own. If yours are somewhere
else, create the git-ignored `Directory.Build.props.user` file in the repository root:

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

That works the same on Windows and Linux. `dotnet clean LinuxCompat.sln` removes the build
output. There are no platform-specific build scripts in the repository.

Whenever the Pulsar path is available, the client project deploys `plugin.dll` and `plugin.xml`
to `~/.config/Pulsar/Modern/Local/linux-compat`.

## Formatting

Install CSharpier once, then format the repository before each commit:

```bash
dotnet tool install -g csharpier
csharpier format .
```

`csharpier check .` verifies the formatting without touching any files. `.csharpierignore`
keeps CSharpier on C# source and off the build output.

## Verification

`Checks/InstallSmoke` installs the full `Finish` Harmony category against the shipped binaries.
That runs every transpiler and checks its IL anchors, all without starting the game. It also
applies the `VRage.Steam` Cecil rewrite and prepares the rewritten methods. The check fails when
the number of patched methods changes, so if you add or remove a patch, update the expected count
in `Checks/InstallSmoke/Program.cs`.

```bash
dotnet build Checks/InstallSmoke/InstallSmoke.csproj -p:Game2=/path/to/SpaceEngineers2/Game2
COMPlus_ReadyToRun=0 \
SE2_NATIVE_DIR=/dir/with/all/so/files \
PULSAR_LIBRARIES=/path/to/Pulsar/Libraries/Modern \
  dotnet Checks/InstallSmoke/bin/Debug/net10.0/InstallSmoke.dll \
  /path/to/SpaceEngineers2/Game2
```

`COMPlus_ReadyToRun=0` is required. Without it the types in the rewritten `VRage.Steam`
assembly do not resolve and the prepatch check fails.
