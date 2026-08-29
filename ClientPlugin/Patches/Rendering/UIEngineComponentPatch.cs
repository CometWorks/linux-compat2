using HarmonyLib;
using Keen.VRage.Core.Render;
using Keen.VRage.UI.EngineComponents;

namespace LinuxCompat.Patches.Rendering;

[HarmonyPatch(typeof(UIEngineComponent), nameof(UIEngineComponent.UIManagerTick))]
[HarmonyPatchCategory("Finish")]
internal static class UIEngineComponentPatch
{
    private static readonly object PendingLock = new();
    private static RenderDisplaySettings? _pending;

    public static void RecordDisplaySettings(in RenderDisplaySettings settings)
    {
        lock (PendingLock)
            _pending = settings;
    }

    static void Prefix(UIEngineComponent __instance)
    {
        if (!__instance._initialized.IsSet)
            return;

        RenderDisplaySettings? pending;
        lock (PendingLock)
        {
            pending = _pending;
            _pending = null;
        }
        if (pending is { } settings)
        {
            MainUISystemPatch.LayoutUpdated(settings.Resolution);
            __instance.OnDisplaySettingsChanged(settings);
        }
    }
}
