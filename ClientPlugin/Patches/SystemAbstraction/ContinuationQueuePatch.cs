using System.Diagnostics;
using HarmonyLib;
using Keen.VRage.Library.Threading;

namespace ClientPlugin.Patches.SystemAbstraction;

// Without a time provider the queue converts Stopwatch.GetTimestamp() * 10000000 / Frequency,
// and the multiplication wraps around: on Linux after about 15 minutes of uptime and then every
// 31 minutes. Delayed continuations queued just before a wrap, like the render asset manager's
// resource finalization, then wait until the value climbs back, up to half an hour.
[HarmonyPatch(typeof(ContinuationQueue), nameof(ContinuationQueue.Ticks), MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
internal static class ContinuationQueueTicksPatch
{
    static bool Prefix(ContinuationQueue __instance, ref long __result)
    {
        __result = __instance.TimeProvider?.FrameTime.Ticks ?? Stopwatch.GetElapsedTime(0).Ticks;
        return false;
    }
}
