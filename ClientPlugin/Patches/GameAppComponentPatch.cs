using LinuxCompat.Platform;

namespace LinuxCompat.Patches;

public static class GameAppComponentPatch
{
    /// <summary>
    /// The eight-second UI resource pin wait assumes hardware GPU pipeline compilation
    /// speed. In explicit Linux CPU-rendering mode give each pin wait a two-minute budget.
    /// </summary>
    public static void Prefix(ref TimeSpan waitTime)
    {
        if (LinuxNativeLibraryResolver.IsEnabled("SE2_CPU_RENDERING"))
            waitTime = TimeSpan.FromMinutes(2);
    }
}
