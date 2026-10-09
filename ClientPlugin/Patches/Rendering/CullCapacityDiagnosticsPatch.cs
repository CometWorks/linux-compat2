using HarmonyLib;
using Keen.VRage.Render12.Core;
using Keen.VRage.Render12.Core.Systems;
using Keen.VRage.Render12.Core.Systems.CommonResources;

namespace ClientPlugin.Patches.Rendering;

/// <summary>
/// Diagnostics for SE2-0003 (shadow pass crash: the instance index buffer outgrows the
/// 64 MB direct buffer limit). Not for release.
///
/// The geometry draw buffer capacities come from <c>GeometryContext.UpdateRanges</c>, which
/// on adapters without limited indirect commands sizes every material range from the
/// scene-wide <c>CullCapacityTrackingManager.TotalMaterialStateUsages</c>, not from culling.
/// This logs those totals next to the capacities whenever a capacity changes, and logs the
/// request that triggers each instance index buffer resize.
/// </summary>
[HarmonyPatch(typeof(SceneDrawSystem), "EnsureRangesOutputGeometryBuffers")]
[HarmonyPatchCategory("Finish")]
internal static class CullCapacityDiagnosticsPatch
{
    private static string _lastCapacities = "";
    private static long _lastLogTicks;

    private static void Postfix()
    {
        string capacities = Capacities();
        long now = Environment.TickCount64;
        if (capacities == _lastCapacities && now - _lastLogTicks < 60_000)
            return;
        _lastCapacities = capacities;
        _lastLogTicks = now;
        CoreSystems.Log.WriteLine($"[SE2-0003] {capacities} {Totals()}");
    }

    internal static string Capacities()
    {
        // The index buffer is first sized in the CommonResourcesManager constructor,
        // before the draw contexts exist.
        if (CoreSystems.DrawContexts == null)
            return "cap (no draw contexts yet)";
        OutputGeometryBufferContext main = CoreSystems.DrawContexts.MainOutputGeometryBuffers;
        OutputGeometryBufferContext fx = CoreSystems.DrawContexts.MainOutputEffectGeometryBuffers;
        return $"cap main single={main._instanceBuffersFirstPass.Capacity}/{main._instanceBuffersSecondPass.Capacity}"
            + $" instanced={main._instancedInstanceBuffersFirstPass.Capacity}/{main._instancedInstanceBuffersSecondPass.Capacity}"
            + $" volume={main._volumeInstanceBuffersFirstPass.Capacity}/{main._volumeInstanceBuffersSecondPass.Capacity}"
            + $" fx single={fx._instanceBuffersFirstPass.Capacity} instanced={fx._instancedInstanceBuffersFirstPass.Capacity}"
            + $" indexBuffer={CoreSystems.CommonResources?._largestIndexBuffer?.ElementCount ?? 0}";
    }

    private static string Totals()
    {
        ReadOnlySpan<uint> totals = CoreSystems
            .CullCapacityTrackingManager
            .TotalMaterialStateUsages;
        ReadOnlySpan<bool> instanced = CoreSystems.Materials.MaterialStatesIsInstanced;
        ReadOnlySpan<bool> volume = CoreSystems.Materials.MaterialStatesIsVolume;
        int count = CoreSystems.Materials.MaxUsedMaterialStateId + 1;
        long single = 0,
            inst = 0,
            vol = 0;
        var top = new List<(uint Usage, int Id, char Kind)>();
        for (int i = 0; i < count; i++)
        {
            char kind =
                instanced[i] ? 'I'
                : volume[i] ? 'V'
                : 'S';
            if (kind == 'I')
                inst += totals[i];
            else if (kind == 'V')
                vol += totals[i];
            else
                single += totals[i];
            top.Add((totals[i], i, kind));
        }
        string topText = string.Join(
            ",",
            top.OrderByDescending(t => t.Usage).Take(6).Select(t => $"{t.Id}{t.Kind}:{t.Usage}")
        );
        return $"| totals single={single} instanced={inst} volume={vol} states={count} top={topText}"
            + $" | limitedIndirect={CoreSystems.DeviceWrap.NeedsLimitedIndirectCommands}"
            + $" forceSafeFrustum={CoreSystems.Settings.System.ForceSafeFrustumHeuristic}"
            + $" rootLimitedShadowMask={CoreSystems.Settings.System.EnableShadowMaskLimitedToRootEntity}";
    }
}

[HarmonyPatch(typeof(CommonResourcesManager), "ResizeInstanceIndexBuffer")]
[HarmonyPatchCategory("Finish")]
internal static class InstanceIndexBufferResizeDiagnosticsPatch
{
    private static void Prefix(int requestedSize) =>
        CoreSystems.Log.WriteLine(
            $"[SE2-0003] instance index buffer resize to {requestedSize} ({(long)requestedSize * 4 / (1024 * 1024)} MB), "
                + CullCapacityDiagnosticsPatch.Capacities()
        );
}
