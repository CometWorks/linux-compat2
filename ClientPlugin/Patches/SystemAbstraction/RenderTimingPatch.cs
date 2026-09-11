using System.Diagnostics;
using HarmonyLib;
using Keen.VRage.Render12.Core.Systems;

namespace ClientPlugin.Patches.SystemAbstraction;

[HarmonyPatch(typeof(FramePacer), nameof(FramePacer.OnUpdatedRenderWorkTime))]
[HarmonyPatchCategory("Finish")]
internal static class RenderTimingPatch
{
    static void Prefix(ref TimeSpan renderFrameDelta)
    {
        renderFrameDelta = Stopwatch.GetElapsedTime(0, renderFrameDelta.Ticks);
    }
}
