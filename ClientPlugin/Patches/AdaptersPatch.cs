using Keen.VRage.Core.Render;
using LinuxCompat.Platform;

namespace LinuxCompat.Patches;

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
        if (!OperatingSystem.IsLinux() || __result is not { } adapter)
            return;

        if (VulkanAdapterTypes.IsIntegrated(adapter.DeviceName) is not { } isIntegrated
            || adapter.IsIntegrated == isIntegrated)
            return;

        adapter.IsIntegrated = isIntegrated;
        __result = adapter;
        Console.WriteLine($"[LinuxCompat] Adapter '{adapter.DeviceName}' reported as "
            + (isIntegrated ? "integrated" : "discrete") + " by Vulkan.");
    }

    /// <summary>
    /// Linux CPU rendering cannot afford the throw-away adapter probe device that
    /// <c>Adapters.CreateSupportedDevice</c> builds for every enumerated adapter.
    /// </summary>
    public static bool SkipProbeDevice()
        => OperatingSystem.IsLinux() && LinuxNativeLibraryResolver.IsEnabled("SE2_CPU_RENDERING");

    /// <summary>
    /// Reports feature level 12.0 support when the probe device was deliberately skipped.
    /// </summary>
    public static bool IsFeatureLevelSupported(bool probeDeviceCreated)
        => probeDeviceCreated || SkipProbeDevice();

    public static bool FeatureAnalysisPrefix(ref bool deviceSupported, ref bool doublePrecision,
        ref bool rayTracing, ref bool isIntegrated)
    {
        if (!OperatingSystem.IsLinux() || !LinuxNativeLibraryResolver.IsEnabled("SE2_CPU_RENDERING"))
            return true;

        deviceSupported = false;
        doublePrecision = false;
        rayTracing = false;
        isIntegrated = true;
        return false;
    }
}
