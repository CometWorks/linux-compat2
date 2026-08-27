using HarmonyLib;
using Keen.VRage.Render.Contracts;

namespace LinuxCompat.Patches.Rendering;

[HarmonyPatch(typeof(PersistentDrawBatch), nameof(PersistentDrawBatch.Submit))]
[HarmonyPatchCategory("Finish")]
internal static class PersistentDrawBatchPatch
{
    static void Postfix(PersistentDrawBatch __instance) =>
        MainUISystemPatch.RecordSubmittedBatch(__instance.CommandBuffer);
}
