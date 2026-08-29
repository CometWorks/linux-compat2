using System.Diagnostics;
using HarmonyLib;
using Keen.VRage.Animation.Client.GameObjects.Budgeting;

namespace LinuxCompat.Patches.SystemAbstraction;

[HarmonyPatch(
    typeof(AnimationBudgetSessionComponent.AnimatorRuntimeAccumulator),
    nameof(AnimationBudgetSessionComponent.AnimatorRuntimeAccumulator.RecordRuntime)
)]
[HarmonyPatchCategory("Finish")]
internal static class AnimationTimingPatch
{
    static void Prefix(ref TimeSpan runtime)
    {
        runtime = Stopwatch.GetElapsedTime(0, runtime.Ticks);
    }
}
