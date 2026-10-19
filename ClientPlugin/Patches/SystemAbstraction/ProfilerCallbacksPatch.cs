using System.Reflection;
using HarmonyLib;
using Keen.VRage.Library.Diagnostics;

namespace ClientPlugin.Patches.SystemAbstraction;

/// <summary>
/// The exposed profiler callbacks are raw exports of Optick.dll, which has no Linux build.
/// Native code receiving them (the Kythera bridge) gets null pointers instead of a
/// DllNotFoundException at engine init.
/// </summary>
[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
internal static class ProfilerCallbacksPatch
{
    static IEnumerable<MethodBase> TargetMethods() =>
        [
            AccessTools.PropertyGetter(
                typeof(Profiler.ExposedCallbacks),
                nameof(Profiler.ExposedCallbacks.BeginMethod)
            ),
            AccessTools.PropertyGetter(
                typeof(Profiler.ExposedCallbacks),
                nameof(Profiler.ExposedCallbacks.EndMethod)
            ),
            AccessTools.PropertyGetter(
                typeof(Profiler.ExposedCallbacks),
                nameof(Profiler.ExposedCallbacks.RegisterCounterMethod)
            ),
            AccessTools.PropertyGetter(
                typeof(Profiler.ExposedCallbacks),
                nameof(Profiler.ExposedCallbacks.SetCounterMethod)
            ),
        ];

    static bool Prefix(ref nint __result)
    {
        __result = 0;
        return false;
    }
}
