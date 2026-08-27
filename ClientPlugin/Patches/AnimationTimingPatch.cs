using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace LinuxCompat.Patches;

public static class AnimationTimingPatch
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
        if (assembly.GetName().Name != "VRage.Animation.Client" || Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        Type accumulator = assembly.GetType(
            "Keen.VRage.Animation.Client.GameObjects.Budgeting.AnimationBudgetSessionComponent+AnimatorRuntimeAccumulator",
            throwOnError: true)!;
        new Harmony("LinuxCompat.AnimationTiming").Patch(
            AccessTools.DeclaredMethod(accumulator, "RecordRuntime", [typeof(TimeSpan)])!,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(AnimationTimingPatch), nameof(Prefix))!));
    }

    private static void Prefix(ref TimeSpan __0)
    {
        __0 = Stopwatch.GetElapsedTime(0, __0.Ticks);
    }
}
