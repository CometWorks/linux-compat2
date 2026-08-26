using System.Diagnostics;
using HarmonyLib;
using Keen.VRage.Library.Utils;

namespace LinuxCompat.Patches;

public static class AppTimerPatch
{
    public static void Install()
    {
        var harmony = new Harmony("LinuxCompat.AppTimer");
        harmony.Patch(AccessTools.PropertyGetter(typeof(AppTimer), nameof(AppTimer.ElapsedTicks)),
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(AppTimerPatch), nameof(ElapsedTicksPrefix))!));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(AppTimer), nameof(AppTimer.AddElapsed))!,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(AppTimerPatch), nameof(AddElapsedPrefix))!));
        harmony.Patch(AccessTools.PropertyGetter(typeof(WaitForTargetFrameRate), nameof(WaitForTargetFrameRate.TicksPerFrame)),
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(AppTimerPatch), nameof(TicksPerFramePrefix))!));
    }

    private static bool ElapsedTicksPrefix(ref long __result, long ____startTicks, long ____elapsedTicks)
    {
        if (!OperatingSystem.IsLinux())
            return true;

        __result = ____elapsedTicks + Stopwatch.GetElapsedTime(____startTicks).Ticks;
        return false;
    }

    private static bool AddElapsedPrefix(TimeSpan timespan, ref long ____startTicks)
    {
        if (!OperatingSystem.IsLinux())
            return true;

        ____startTicks -= (long)(timespan.TotalSeconds * Stopwatch.Frequency);
        return false;
    }

    private static bool TicksPerFramePrefix(WaitForTargetFrameRate __instance, ref long __result)
    {
        if (!OperatingSystem.IsLinux())
            return true;

        __result = (long)Math.Round(TimeSpan.TicksPerSecond / __instance.TargetFrequency);
        return false;
    }
}
