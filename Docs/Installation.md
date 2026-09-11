# Installation

[← README](../README.md)

## Prerequisites

- Space Engineers 2 installed by the native Linux Steam client
- Pulsar with its `Modern.bin` Linux executable, normally under `~/.config/Pulsar`
- .NET 10 runtime
- A Vulkan-capable graphics driver
- The two dependency archives declared assets in `ClientPlugin/ClientPlugin.xml`
  (`se2-dependencies.tar.gz` and `se2-native-wrappers.tar.gz`; their paths are relative to the
  repository root)

## System packages

The dependency archives carry DXVK, vkd3d-proton, SDL3, DXC, FMOD, FidelityFX and the PE-loader
wrappers, but a few libraries have to come from your distribution. The plugin checks for them on
startup and reports the missing ones by name, together with the package names below.

| Library | Debian/Ubuntu | Fedora | Arch |
| --- | --- | --- | --- |
| `libvulkan.so.1` (plus a Vulkan driver for your GPU) | `libvulkan1`, `mesa-vulkan-drivers` | `vulkan-loader`, `mesa-vulkan-drivers` | `vulkan-icd-loader`, `vulkan-radeon` / `vulkan-intel` / `nvidia-utils` |
| X11: `libX11.so.6`, `libXext.so.6` | `libx11-6`, `libxext6` | `libX11`, `libXext` | `libx11`, `libxext` |
| Wayland: `libwayland-client.so.0`, `libwayland-cursor.so.0`, `libwayland-egl.so.1`, `libxkbcommon.so.0` | `libwayland-client0`, `libwayland-cursor0`, `libwayland-egl1`, `libxkbcommon0` | `libwayland-client`, `libwayland-cursor`, `libwayland-egl`, `libxkbcommon` | `wayland`, `libxkbcommon` |
| Audio, one of: `libpulse.so.0` (PulseAudio or PipeWire), `libasound.so.2` | `libpulse0`, `libasound2` | `pulseaudio-libs`, `alsa-lib` | `libpulse`, `alsa-lib` |

Either the X11 or the Wayland set is enough. [`SDL_VIDEODRIVER`](Running.md#environment-variables)
restricts the check to that driver, and `offscreen` needs neither.

If the audio backend is missing, the game still starts with a warning, but without audio.

If FidelityFX is missing, then FSR upscaling won't be available.
