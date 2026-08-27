using System.Reflection;
using HarmonyLib;

namespace LinuxCompat.Patches;

public static class BatteryStatusPatch
{
    private static int _installed;

    public static void Install()
    {
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            TryPatch(assembly);
    }

    public static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args) => TryPatch(args.LoadedAssembly);

    private static void TryPatch(Assembly assembly)
    {
        if (assembly.GetName().Name != "Game2.Client" || Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        Type batteryStatus = assembly.GetType("Keen.Game2.Client.RuntimeSystems.BatteryStatus", throwOnError: true)!;
        new Harmony("LinuxCompat.BatteryStatus").Patch(
            AccessTools.DeclaredMethod(batteryStatus, "IsOnBattery", Type.EmptyTypes)!,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(BatteryStatusPatch), nameof(Prefix))!));
    }
}
