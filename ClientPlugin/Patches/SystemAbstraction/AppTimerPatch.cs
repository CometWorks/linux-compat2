using System.Diagnostics;
using HarmonyLib;
using Keen.VRage.Library.Utils;

namespace LinuxCompat.Patches.SystemAbstraction;

[HarmonyPatch(typeof(AppTimer), nameof(AppTimer.ElapsedTicks), MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
internal static class AppTimerElapsedTicksPatch
{
    static bool Prefix(AppTimer __instance, ref long __result)
    {
        __result =
            __instance._elapsedTicks + Stopwatch.GetElapsedTime(__instance._startTicks).Ticks;
        return false;
    }
}

[HarmonyPatch(typeof(AppTimer), nameof(AppTimer.AddElapsed))]
[HarmonyPatchCategory("Finish")]
internal static class AppTimerAddElapsedPatch
{
    static bool Prefix(AppTimer __instance, TimeSpan timespan)
    {
        __instance._startTicks -= (long)(timespan.TotalSeconds * Stopwatch.Frequency);
        return false;
    }
}

[HarmonyPatch(
    typeof(WaitForTargetFrameRate),
    nameof(WaitForTargetFrameRate.TicksPerFrame),
    MethodType.Getter
)]
[HarmonyPatchCategory("Finish")]
internal static class WaitForTargetFrameRateTicksPerFramePatch
{
    static bool Prefix(WaitForTargetFrameRate __instance, ref long __result)
    {
        __result = (long)Math.Round(TimeSpan.TicksPerSecond / __instance.TargetFrequency);
        return false;
    }
}
