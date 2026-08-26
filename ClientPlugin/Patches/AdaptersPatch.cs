using LinuxCompat.Platform;

namespace LinuxCompat.Patches;

public static class AdaptersPatch
{
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
