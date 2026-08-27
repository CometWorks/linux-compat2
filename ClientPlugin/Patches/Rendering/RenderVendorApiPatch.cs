using HarmonyLib;
using Keen.VRage.Render12.Utils;

namespace LinuxCompat.Patches.Rendering;

[HarmonyPatch(typeof(NvApi), nameof(NvApi.Initialize))]
[HarmonyPatchCategory("Finish")]
internal static class NvApiInitializePatch
{
    static bool Prefix() => false;
}

[HarmonyPatch(typeof(NvApi), nameof(NvApi.IsInitialized))]
[HarmonyPatchCategory("Finish")]
internal static class NvApiIsInitializedPatch
{
    static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(AGS), nameof(AGS.Initialize))]
[HarmonyPatchCategory("Finish")]
internal static class AgsInitializePatch
{
    static bool Prefix() => false;
}

// Game 2.4 replaced AGS.QueryTeraflops(string)/(int,int) with two QueryAdapterDetails
// overloads returning the internal AGS.AdapterDetails?. Both assert on the AGS context
// that the skipped Initialize never creates, so they must decline before running.
[HarmonyPatch(typeof(AGS), nameof(AGS.QueryAdapterDetails), typeof(string))]
[HarmonyPatchCategory("Finish")]
internal static class AgsQueryAdapterDetailsByNamePatch
{
    static bool Prefix() => false;
}

[HarmonyPatch(typeof(AGS), nameof(AGS.QueryAdapterDetails), typeof(int), typeof(int))]
[HarmonyPatchCategory("Finish")]
internal static class AgsQueryAdapterDetailsByIdPatch
{
    static bool Prefix() => false;
}
