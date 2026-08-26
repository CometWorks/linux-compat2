using HarmonyLib;
using Keen.VRage.Render.Contracts;

namespace LinuxCompat.Patches;

public static class PersistentDrawBatchPatch
{
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        new Harmony("LinuxCompat.PersistentDrawBatch").Patch(
            AccessTools.DeclaredMethod(typeof(PersistentDrawBatch), nameof(PersistentDrawBatch.Submit), Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(PersistentDrawBatch).FullName, nameof(PersistentDrawBatch.Submit)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(PersistentDrawBatchPatch), nameof(Postfix))!));
    }

    public static void Postfix(PersistentDrawBatch __instance) =>
        MainUISystemPatch.RecordSubmittedBatch(__instance.CommandBuffer);
}
