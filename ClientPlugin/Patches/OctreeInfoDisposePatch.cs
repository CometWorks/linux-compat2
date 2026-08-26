using System.Reflection;
using HarmonyLib;

namespace LinuxCompat.Patches;

public static class OctreeInfoDisposePatch
{
    private static FieldInfo? _loader;
    private static int _installed;

    public static void Install()
    {
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            TryPatch(assembly);
    }

    public static bool Prefix(object __instance) => _loader!.GetValue(__instance) != null;

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args) => TryPatch(args.LoadedAssembly);

    private static void TryPatch(Assembly assembly)
    {
        if (assembly.GetName().Name != "VRage.Voxels" || Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        Type streaming = assembly.GetType(
            "Keen.VRage.Voxels.EngineComponents.Streaming.OctreeRegionStreamingComponent",
            throwOnError: true)!;
        Type octreeInfo = AccessTools.Inner(streaming, "OctreeInfoInternal")!;
        _loader = AccessTools.DeclaredField(octreeInfo, "_loader")!;
        new Harmony("LinuxCompat.OctreeInfoDispose").Patch(
            AccessTools.DeclaredMethod(octreeInfo, nameof(IDisposable.Dispose), Type.EmptyTypes)!,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(OctreeInfoDisposePatch), nameof(Prefix))!));
    }
}
