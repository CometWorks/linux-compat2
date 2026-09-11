# Running the game

[← README](../README.md)

## Launch

```bash
~/.config/Pulsar/Modern.bin -noUpdate -sources -noPrompt -game2 /path/to/SpaceEngineers2/Game2
```

Before `Program.Main` runs, the preloader converts the shipped ReadyToRun assemblies listed in
`ClientPlugin/ReadyToRun.cs` to IL-only images in Pulsar's preloader cache, then installs every
Harmony patch. The `VRage.Steam` Cecil rewrite runs on that assembly either way.

## Environment variables

A normal run needs none of these. All of them are read at startup, and unless a row says
otherwise, a variable counts as on when it is set to `1` or `true`.

| Variable | Effect |
| --- | --- |
| `SE2_DISABLE_FORCED_REDOWNLOAD` | Stops the forced mod re-download, see below. Off by default. |
| `SE2_CPU_RENDERING` | Software rendering through llvmpipe: selects the lavapipe Vulkan driver, points DXVK and vkd3d at it, caps the feature level at 12.0, limits llvmpipe to at most four threads, skips the per-adapter probe device, and raises the UI resource wait from eight seconds to two minutes. |
| `SE2_MAX_FPS` | Frame-rate cap. Accepts an integer from 1 to 240; anything else is ignored. |
| `SE2_NATIVE_DIR` | Directory searched for the bundled native libraries before the plugin's own directory. |
| `SE2_INPUT_TEST` | Logs `SE2_INPUT COMPLETE` once an Escape press and release, mouse motion, and a left-click press and release have all been seen. For confirming input in an automated run. |
| `SE2_PLUGIN_DISABLE_METHOD_VERIFICATION` | Any value other than `0` skips the transpiler IL verification in `ClientPlugin/Tools/TranspilerHelpers.cs`. |
| `SDL_VIDEODRIVER` | `x11` or `wayland` narrows the startup window-system check to that driver; any other value (`offscreen`, `dummy`) skips it. Also passed straight through to SDL3. |
| `DOTNET_ReadyToRun`, `COMPlus_ReadyToRun` | `0` or `false` skips the ReadyToRun rewrite; only the `VRage.Steam` rewrite still runs. |

To swap out a single bundled native library, point `SE2_<NAME>_LIBRARY` at an absolute path.
`<NAME>` is one of `SDL3`, `PHYSICS`, `VOXELS`, `SLUG`, `DXGI`, `D3D12`, `D3D12CORE`,
`DXCOMPILER`, `STEAM_API`, `KYTHERA`, `FMOD`, `FMOD_STUDIO`, or `FIDELITYFX`. The plugin also
sets `DXVK_WSI_DRIVER=SDL3` for itself.

## Loading a world with many workshop mods

Space Engineers 2 re-downloads every mod of a world on every load, even when Steam already has
the content installed and current: `SteamUGCServiceComponent.GetModDataFilesystemAsync` resolves
each mod with `DownloadItem(id, force: true)`. Steam serves a handful of items that way and then
refuses the rest, and one refusal aborts the whole load, so a world with more than a few mods is
not reliably loadable. That is the game's behaviour, not a Linux one.

Setting `SE2_DISABLE_FORCED_REDOWNLOAD=1` makes the preloader neutralise that `force` argument,
which puts `DownloadItem`'s own guard back in play: content that is installed and not flagged for
update is used as it is, and anything missing or out of date is still downloaded before it is
mounted.

```bash
SE2_DISABLE_FORCED_REDOWNLOAD=1 ~/.config/Pulsar/Modern.bin -noUpdate -sources -noPrompt \
  -game2 /path/to/SpaceEngineers2/Game2
```

The switch is opt-in because it changes how the game talks to Steam rather than how it runs on
Linux. When it takes effect, the log says so on one line.
