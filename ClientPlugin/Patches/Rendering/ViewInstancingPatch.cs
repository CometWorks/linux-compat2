using HarmonyLib;
using Keen.VRage.Core.Platform;
using Keen.VRage.Core.Render;
using Keen.VRage.Render12.Core.Device;

namespace ClientPlugin.Patches.Rendering;

/// <summary>
/// Game 4.0 renders cube and mask shadows with D3D12 view instancing, mapping every view to
/// render target array index 0. vkd3d-proton reports view instancing Tier 1 but rejects that
/// layout, so every material with a shadow pass fails to compile and draws magenta. Reporting
/// view instancing as unsupported selects the game's own fan-out shadow passes instead.
/// </summary>
[HarmonyPatch(
    typeof(DeviceWrap),
    MethodType.Constructor,
    typeof(AdapterInfo),
    typeof(IPlatformRender),
    typeof(IPlatformSystem),
    typeof(IPlatformWindows)
)]
[HarmonyPatchCategory("Finish")]
internal static class ViewInstancingPatch
{
    static void Postfix(DeviceWrap __instance) => __instance.IsViewInstancingSupported = false;
}
