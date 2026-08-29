using HarmonyLib;
using Keen.Game2.Client.RuntimeSystems;

namespace LinuxCompat.Patches.SystemAbstraction;

[HarmonyPatch(typeof(BatteryStatus), nameof(BatteryStatus.IsOnBattery))]
[HarmonyPatchCategory("Finish")]
internal static class BatteryStatusPatch
{
    static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}
