using System.Runtime.InteropServices;

namespace ClientPlugin.Platform;

// Probes every native library the plugin needs before the game touches any of
// them, so a missing package is reported by name instead of surfacing later as
// a bare DllNotFoundException or a resolver FileNotFoundException.
internal static class DependencyCheck
{
    // Packages: Debian/Ubuntu | Fedora | Arch
    private sealed record SystemLibrary(string Name, string Purpose, string Packages);

    // P/Invoke names the resolver substitutes; the game cannot start without them.
    private static readonly string[] Bundled =
    [
        "SDL3",
        "dxgi",
        "d3d12",
        "d3d12core",
        "dxcompiler",
        "fmod",
        "fmodstudio",
        "VRage.KytheraV2.Native.dll",
        "VRage.Physics.Native.dll",
        "VRage.Voxels.Native.dll",
        "VRage.Slug.Native.dll",
    ];

    private static readonly SystemLibrary Vulkan = new(
        "libvulkan.so.1",
        "Vulkan loader for DXVK and vkd3d-proton",
        "libvulkan1 | vulkan-loader | vulkan-icd-loader"
    );

    private static readonly SystemLibrary[] X11 =
    [
        new("libX11.so.6", "X11 client", "libx11-6 | libX11 | libx11"),
        new("libXext.so.6", "X11 extensions", "libxext6 | libXext | libxext"),
    ];

    private static readonly SystemLibrary[] Wayland =
    [
        new(
            "libwayland-client.so.0",
            "Wayland client",
            "libwayland-client0 | libwayland-client | wayland"
        ),
        new(
            "libwayland-cursor.so.0",
            "Wayland cursor",
            "libwayland-cursor0 | libwayland-cursor | wayland"
        ),
        new("libwayland-egl.so.1", "Wayland EGL", "libwayland-egl1 | libwayland-egl | wayland"),
        new(
            "libxkbcommon.so.0",
            "keyboard handling",
            "libxkbcommon0 | libxkbcommon | libxkbcommon"
        ),
    ];

    // FMOD needs one of these; without any the game runs silent.
    private static readonly SystemLibrary[] Audio =
    [
        new("libpulse.so.0", "PulseAudio or PipeWire", "libpulse0 | pulseaudio-libs | libpulse"),
        new("libasound.so.2", "ALSA", "libasound2 | alsa-lib | alsa-lib"),
    ];

    internal static void Run()
    {
        var missing = Bundled
            .Select(LinuxNativeLibraryResolver.MapNativeLibrary)
            .Where(path => !File.Exists(path))
            .Select(path => $"{Path.GetFileName(path)}: not bundled with the plugin (reinstall it)")
            .ToList();
        if (missing.Count > 0)
            missing.Add("searched: " + string.Join(", ", LinuxNativeLibraryResolver.Directories));
        missing.AddRange(Missing([Vulkan]));
        missing.AddRange(MissingWindowSystem());

        if (!File.Exists(LinuxNativeLibraryResolver.MapNativeLibrary("amd_fidelityfx_loader_dx12")))
            Console.WriteLine(
                "[LinuxCompat] WARNING: libamd_fidelityfx_loader_dx12.so is missing, FSR upscaling will be unavailable"
            );
        if (Audio.All(library => !CanLoad(library.Name)))
            Console.WriteLine(
                "[LinuxCompat] WARNING: no audio backend, the game will have no sound; install one of "
                    + string.Join(", ", Audio.Select(Describe))
            );

        if (missing.Count == 0)
        {
            Console.WriteLine("[LinuxCompat] all native dependencies found");
            return;
        }

        throw new DllNotFoundException(
            "Missing native dependencies (packages: Debian/Ubuntu | Fedora | Arch):\n  "
                + string.Join("\n  ", missing)
                + "\nInstall the missing packages, then start the game again."
        );
    }

    private static IEnumerable<string> MissingWindowSystem()
    {
        var x11 = Missing(X11).ToList();
        var wayland = Missing(Wayland).ToList();
        switch (Environment.GetEnvironmentVariable("SDL_VIDEODRIVER")?.ToLowerInvariant())
        {
            case "x11":
                return x11;
            case "wayland":
                return wayland;
            case null or "":
                break;
            default:
                // offscreen, dummy: no display needed
                return [];
        }

        // SDL3 picks whichever driver works, so one complete set is enough.
        if (x11.Count == 0 || wayland.Count == 0)
            return [];

        return
        [
            "no usable window system, install either\n    X11: "
                + string.Join(", ", X11.Select(Describe))
                + "\n    Wayland: "
                + string.Join(", ", Wayland.Select(Describe)),
        ];
    }

    private static IEnumerable<string> Missing(IEnumerable<SystemLibrary> libraries) =>
        libraries
            .Where(library => !CanLoad(library.Name))
            .Select(library => $"{library.Name}: {library.Purpose} ({library.Packages})");

    private static string Describe(SystemLibrary library) => $"{library.Name} ({library.Packages})";

    private static bool CanLoad(string name) => NativeLibrary.TryLoad(name, out _);
}
