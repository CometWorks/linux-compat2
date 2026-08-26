using Keen.VRage.Core.Render;
using Keen.VRage.Core.Platform;
using LinuxCompat.Platform;

namespace LinuxCompat.Patches;

public static class SwapChainPatch
{
    public static void Prefix(in RenderDisplaySettings settings, nint windowHandle)
    {
        if (OperatingSystem.IsLinux())
            SdlPlatformWindow.PrepareForSwapChain(windowHandle, settings.Resolution);
    }

    public static void UpdatePrefix(IPlatformWindows ____windows,
        RenderDisplaySettings ____currentDisplaySettings,
        ref RenderDisplaySettings? ____requestedDisplaySettings)
    {
        if (!OperatingSystem.IsLinux()
            || ____windows.Window is not SdlPlatformWindow window
            || !window.TryConsumeDrawableResize(out var resolution))
            return;

        if (____requestedDisplaySettings is { } pending
            && (pending.FullscreenMode != ____currentDisplaySettings.FullscreenMode
                || pending.Resolution != ____currentDisplaySettings.Resolution))
            return;
        RenderDisplaySettings settings = ____requestedDisplaySettings ?? ____currentDisplaySettings;
        if (settings.FullscreenMode || settings.Resolution == resolution)
            return;
        settings.Resolution = resolution;
        ____requestedDisplaySettings = settings;
    }
}
