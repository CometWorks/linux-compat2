# Linux compatibility for Space Engineers 2

LinuxCompat patches the stock Space Engineers 2 binaries so the game runs natively on Linux
through [Pulsar](https://github.com/SpaceGT/Pulsar)'s Modern executable. Harmony and Pulsar's
Cecil preloader apply the patches at run time; the plugin ships no recompiled game assemblies.

Linux wrappers and translation layers stand in for the native Windows libraries, among them
DXVK, vkd3d-proton, SDL3, FMOD and AMD FidelityFX (the FSR 3.1 upscaler, built for Linux).

You need Steam's copy of Space Engineers 2, Pulsar, the .NET 10 runtime and a Vulkan-capable
driver.

## Documentation

- [Installation](Docs/Installation.md): prerequisites and the distribution packages to install
- [Running the game](Docs/Running.md): the launch command,
  [environment variables](Docs/Running.md#environment-variables), and
  [loading a world with many workshop mods](Docs/Running.md#loading-a-world-with-many-workshop-mods)
- [Development](Docs/Development.md): build and Pulsar dev-source setup, formatting, and the
  install smoke check
- [Internals](Docs/Internals.md): patching notes and repository layout

## Bug reports

Open an issue and attach the game and Pulsar logs, or start a support thread on the
[Pulsar Discord](https://discord.gg/z8ZczP2YZY).

## Legal

Space Engineers 2 is a trademark of Keen Software House s.r.o.
