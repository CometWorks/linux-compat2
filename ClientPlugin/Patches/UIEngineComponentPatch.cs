using HarmonyLib;
using Keen.VRage.Core.Render;
using Keen.VRage.Library.Threading;
using Keen.VRage.UI.EngineComponents;

namespace LinuxCompat.Patches;

public static class UIEngineComponentPatch
{
    private static readonly object PendingLock = new();
    private static int _installed;
    private static RenderDisplaySettings? _pending;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        new Harmony("LinuxCompat.UIEngineComponent").Patch(
            AccessTools.DeclaredMethod(typeof(UIEngineComponent), "UIManagerTick", Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(UIEngineComponent).FullName, "UIManagerTick"),
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(UIEngineComponentPatch), nameof(Prefix))!));
    }

    public static void RecordDisplaySettings(in RenderDisplaySettings settings)
    {
        if (!OperatingSystem.IsLinux())
            return;

        lock (PendingLock)
            _pending = settings;
    }

    public static void Prefix(UIEngineComponent __instance, ref AtomicFlag ____initialized)
    {
        if (!____initialized.IsSet)
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
