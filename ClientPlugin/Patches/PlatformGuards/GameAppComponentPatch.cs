using LinuxCompat.Platform;
using VrTask = Keen.VRage.Library.Threading.Task;

namespace LinuxCompat.Patches.PlatformGuards;

public static class GameAppComponentPatch
{
    /// <summary>
    /// The eight-second UI resource pin wait assumes hardware GPU pipeline compilation
    /// speed. In explicit Linux CPU-rendering mode give each pin wait a two-minute budget.
    /// </summary>
    public static bool Prefix(ref TimeSpan waitTime, ref VrTask result)
    {
        if (SdlThread.IsWayland && SdlPlatformWindow.IsVisibilityDeferred)
        {
            result = System.Threading.Tasks.Task.CompletedTask;
            return false;
        }
        if (LinuxNativeLibraryResolver.IsEnabled("SE2_CPU_RENDERING"))
            waitTime = TimeSpan.FromMinutes(2);
        return true;
    }

    public static bool EndOfUiLoadingPrefix(ref VrTask __result)
    {
        if (!SdlThread.IsWayland || !SdlPlatformWindow.IsVisibilityDeferred)
            return true;
        __result = System.Threading.Tasks.Task.CompletedTask;
        return false;
    }
}
