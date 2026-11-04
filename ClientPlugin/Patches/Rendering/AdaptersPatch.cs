using System.Text.RegularExpressions;
using ClientPlugin.Platform;
using Keen.VRage.Core.Render;

namespace ClientPlugin.Patches.Rendering;

public static class AdaptersPatch
{
    /// <summary>
    /// Corrects the integrated-GPU flag of a freshly built <see cref="AdapterInfo"/> from
    /// Vulkan.
    ///
    /// DXVK does not report integrated adapters as integrated, so every Linux adapter arrives
    /// with <c>IsIntegrated == false</c>, and the vendor APIs that would supply a teraflop
    /// figure are skipped on Linux. <c>AdapterInfo.GetBestAdapter</c> then falls past its
    /// teraflops and integrated comparisons all the way down to dedicated memory — where an
    /// APU wins, because it reports a slice of system RAM as dedicated video memory (34 GB on
    /// the reference machine, against the 4090's real 25.7 GB). That ranking both raises the
    /// bogus "Better GPU has been detected" warning and, with no saved adapter preference,
    /// picks the integrated GPU to actually render with.
    ///
    /// Restoring the flag lets the engine's own "prefer the discrete adapter" rule decide,
    /// which is the outcome Windows gets. Adapters Vulkan does not know are left untouched.
    /// </summary>
    public static void FixAdapterType(ref AdapterInfo? __result)
    {
        if (__result is not { } adapter)
            return;

        if (
            VulkanAdapterTypes.IsIntegrated(adapter.DeviceName) is not { } isIntegrated
            || adapter.IsIntegrated == isIntegrated
        )
            return;

        adapter.IsIntegrated = isIntegrated;
        __result = adapter;
        Console.WriteLine(
            $"[LinuxCompat] Adapter '{adapter.DeviceName}' reported as "
                + (isIntegrated ? "integrated" : "discrete")
                + " by Vulkan."
        );
    }

    /// <summary>
    /// Restores the engine's GTX 10x0 workaround, which DXVK's vendor spoof switches off.
    ///
    /// <c>Adapters.CreateAdapterInfo</c> sets <c>NeedsLimitedIndirectCommands</c> for
    /// NVIDIA adapters named like "GTX 1080", which sizes the indirect draw buffers from the
    /// last frame's counts instead of the worst case. DXVK reports NVIDIA GPUs with AMD's
    /// vendor id, so on Linux a Pascal card never matches and gets the full-size buffers.
    /// A GTX 1080 Ti player loses the Vulkan device (Xid 13, compute class) on entering a
    /// world and the desktop hangs with it (SE-997, DIS-0049); the missing workaround is the
    /// likely cause. The adapter name is not spoofed, so Keen's pattern still matches.
    /// </summary>
    public static void RestoreLimitedIndirectCommands(ref AdapterInfo? __result)
    {
        if (
            __result is not { } adapter
            || adapter.SupportDetails.NeedsLimitedIndirectCommands
            || !Regex.IsMatch(adapter.DeviceName, "GTX.*10\\d0", RegexOptions.IgnoreCase)
        )
            return;

        adapter.SupportDetails.NeedsLimitedIndirectCommands = true;
        __result = adapter;
        Console.WriteLine(
            $"[LinuxCompat] Adapter '{adapter.DeviceName}' uses limited indirect commands."
        );
    }

    /// <summary>
    /// Linux CPU rendering cannot afford the throw-away adapter probe device that
    /// <c>Adapters.CreateSupportedDevice</c> builds for every enumerated adapter.
    /// </summary>
    public static bool SkipProbeDevice() =>
        LinuxNativeLibraryResolver.IsEnabled("SE2_CPU_RENDERING");

    /// <summary>
    /// Reports feature level 12.0 support when the probe device was deliberately skipped.
    /// </summary>
    public static bool IsFeatureLevelSupported(bool probeDeviceCreated) =>
        probeDeviceCreated || SkipProbeDevice();

    public static bool FeatureAnalysisPrefix(
        ref bool deviceSupported,
        ref bool doublePrecision,
        ref bool rayTracing,
        ref bool isIntegrated
    )
    {
        if (!LinuxNativeLibraryResolver.IsEnabled("SE2_CPU_RENDERING"))
            return true;

        deviceSupported = false;
        doublePrecision = false;
        rayTracing = false;
        isIntegrated = true;
        return false;
    }
}
