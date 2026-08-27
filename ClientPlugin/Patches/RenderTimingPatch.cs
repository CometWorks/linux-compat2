using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace LinuxCompat.Patches;

internal static class RenderTimingPatch
{
    private static int _installed;

    public static void Install()
    {
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            TryPatch(assembly);
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args) => TryPatch(args.LoadedAssembly);

    private static void TryPatch(Assembly assembly)
    {
        if (assembly.GetName().Name != "VRage.Render12" || Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        Type type = assembly.GetType("Keen.VRage.Render12.Core.Systems.FramePacer", throwOnError: true)!;
        new Harmony("LinuxCompat.RenderTiming").Patch(
            AccessTools.DeclaredMethod(type, "OnUpdatedRenderWorkTime")!,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(RenderTimingPatch), nameof(Prefix))!));
    }

    private static void Prefix(ref TimeSpan __0)
    {
        __0 = Stopwatch.GetElapsedTime(0, __0.Ticks);
    }
}
