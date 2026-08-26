using System.Reflection;
using HarmonyLib;

namespace LinuxCompat.Patches;

internal static class RenderVendorApiPatch
{
    public static void Install()
    {
        Assembly render = Assembly.Load("VRage.Render12");
        Type nvApi = render.GetType("Keen.VRage.Render12.Utils.NvApi", throwOnError: true)!;
        Type ags = render.GetType("Keen.VRage.Render12.Utils.AGS", throwOnError: true)!;
        Harmony harmony = new("LinuxCompat.RenderVendorApis");

        harmony.Patch(AccessTools.DeclaredMethod(nvApi, "Initialize")!,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(RenderVendorApiPatch), nameof(Skip))!));
        harmony.Patch(AccessTools.DeclaredMethod(nvApi, "IsInitialized")!,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(RenderVendorApiPatch), nameof(ReturnFalse))!));
        harmony.Patch(AccessTools.DeclaredMethod(ags, "Initialize")!,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(RenderVendorApiPatch), nameof(Skip))!));

        // Game 2.4 replaced AGS.QueryTeraflops(string)/(int,int) with two QueryAdapterDetails
        // overloads returning the internal AGS.AdapterDetails?. Both assert on the AGS context
        // that the skipped Initialize never creates, so they must decline before running.
        MethodInfo[] queryAdapterDetails = ags.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "QueryAdapterDetails").ToArray();
        if (queryAdapterDetails.Length != 2
            || queryAdapterDetails[0].ReturnType != queryAdapterDetails[1].ReturnType
            || Nullable.GetUnderlyingType(queryAdapterDetails[0].ReturnType) == null)
            throw new MissingMethodException(ags.FullName, "QueryAdapterDetails overloads");
        MethodInfo returnNull = AccessTools.DeclaredMethod(typeof(RenderVendorApiPatch), nameof(ReturnNullValue))!
            .MakeGenericMethod(Nullable.GetUnderlyingType(queryAdapterDetails[0].ReturnType)!);
        foreach (MethodInfo method in queryAdapterDetails)
            harmony.Patch(method, prefix: new HarmonyMethod(returnNull));
    }

    private static bool Skip() => false;

    private static bool ReturnFalse(ref bool __result)
    {
        __result = false;
        return false;
    }

    private static bool ReturnNullValue<T>(ref T? __result) where T : struct
    {
        __result = null;
        return false;
    }
}
