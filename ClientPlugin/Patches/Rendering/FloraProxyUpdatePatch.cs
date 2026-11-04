using HarmonyLib;
using Keen.VRage.Render12.SceneSystem.Components;

namespace ClientPlugin.Patches.Rendering;

/// <summary>
/// Keeps a flora visibility change for an entity that is not on the GPU yet from ending the game
/// with "Sparse update index -1 is out of range" (SE-1004).
/// <para>
/// A flora batch gets its model and instance buffer in <c>PrepareInstanceBuffers</c>, but
/// <c>UploadEntitiesOnGpu</c> allocates its GPU slots only once the model and its materials have
/// loaded. If the batch turns visible or hidden in between, <c>UpdateProxiesOnGpu</c> sees a model
/// and schedules the proxy update at <c>GpuProxyLocation</c> -1, and the failed assertion kills
/// the render thread. The update stays queued, so it fails again every frame. Slow model loading
/// widens the window; on Linux it hit every world load for hours on one night.
/// </para>
/// <para>
/// The prefix hands such an update to the entity's pending upload entry, which the upload reads
/// for the visibility and bounding box, and drops it from the proxy update list. The upload then
/// creates the proxy in its latest state.
/// </para>
/// </summary>
[HarmonyPatch(
    typeof(FloraSectorEntityComponent),
    nameof(FloraSectorEntityComponent.UpdateProxiesOnGpu)
)]
[HarmonyPatchCategory("Finish")]
internal static class FloraProxyUpdatePatch
{
    static void Prefix(FloraSectorEntityComponent __instance)
    {
        var entities = __instance._instancedModelEntities!;
        var pending = __instance._instancedModelEntitiesToAdd!;
        var proxies = __instance._proxiesToUpdate;
        for (int i = 0; i < proxies.Count; i++)
        {
            ref var proxy = ref proxies[i];
            if (proxy.EntityID < 0 || entities[proxy.EntityID].GpuProxyLocation != -1)
                continue;

            for (int j = 0; j < pending.Count; j++)
            {
                ref var upload = ref pending[j];
                if (upload.EntityID != proxy.EntityID)
                    continue;
                upload.IsVisible = proxy.IsVisible;
                upload.BoundingBox = proxy.BoundingBox;
                break;
            }
            proxy.EntityID = -1;
        }
    }
}
