using HarmonyLib;
using Keen.VRage.Voxels.EngineComponents.Streaming;

namespace LinuxCompat.Patches.NullSafety;

[HarmonyPatch(
    typeof(OctreeRegionStreamingComponent.OctreeInfoInternal),
    nameof(OctreeRegionStreamingComponent.OctreeInfoInternal.Dispose)
)]
[HarmonyPatchCategory("Finish")]
internal static class OctreeInfoDisposePatch
{
    static bool Prefix(OctreeRegionStreamingComponent.OctreeInfoInternal __instance) =>
        __instance._loader != null;
}
