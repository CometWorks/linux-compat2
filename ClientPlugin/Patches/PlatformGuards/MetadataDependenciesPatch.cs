using System.Reflection;
using HarmonyLib;
using Keen.VRage.DCS.Internal;
using Keen.VRage.Library.Reflection;
using Keen.VRage.Library.Threading;
using LinuxCompat.Platform;

namespace LinuxCompat.Patches.PlatformGuards;

/// <summary>
/// Keeps VRage.Platform.Windows out of the metadata dependency expansion on Linux and adds
/// the plugin assembly, whose Linux engine components carry DCS annotations, to the scanned
/// set. Implemented without referencing HashSetReader or MetadataDependenciesAttribute at
/// compile time: the Pulsar plugin compiler transitively references VRage.Library.Generator,
/// which defines conflicting copies of both types.
/// </summary>
[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
internal static class MetadataHelperGetAssembliesWithMetadataDependenciesPatch
{
    static MethodBase TargetMethod()
    {
        Type helper = MetadataDependencies.FindType("Keen.VRage.Library.Reflection.MetadataHelper");
        return AccessTools.DeclaredMethod(helper, "GetAssembliesWithMetadataDependencies")
            ?? throw new MissingMethodException(
                helper.FullName,
                "GetAssembliesWithMetadataDependencies"
            );
    }

    static void Prefix(ref IEnumerable<Assembly> __0) =>
        __0 = __0.Append(MetadataDependencies.PluginAssembly).Distinct();
}

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
internal static class MetadataDependenciesAttributePatch
{
    static MethodBase TargetMethod()
    {
        Type attribute = MetadataDependencies.FindType(
            "Keen.VRage.Library.Reflection.MetadataDependenciesAttribute"
        );
        return AccessTools.Constructor(attribute, [typeof(string[])])
            ?? throw new MissingMethodException(attribute.FullName, ".ctor(string[])");
    }

    /// <summary>Attribute instances are materialized through this constructor on every
    /// GetCustomAttribute call, so filtering here keeps the expansion Windows-free.</summary>
    static void Prefix(ref string[] assemblyNames)
    {
        if (
            assemblyNames.Any(name =>
                name.StartsWith("VRage.Platform.Windows,", StringComparison.Ordinal)
            )
        )
        {
            assemblyNames = assemblyNames
                .Where(name =>
                    !name.StartsWith("VRage.Platform.Windows,", StringComparison.Ordinal)
                )
                .ToArray();
        }
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
internal static class MetadataHelperFilterAssembliesWithMetadataPatch
{
    static MethodBase TargetMethod()
    {
        Type helper = MetadataDependencies.FindType("Keen.VRage.Library.Reflection.MetadataHelper");
        return AccessTools.DeclaredMethod(helper, "FilterAssembliesWithMetadata")
            ?? throw new MissingMethodException(helper.FullName, "FilterAssembliesWithMetadata");
    }

    static void Postfix(ref IEnumerable<Assembly> __result) =>
        __result = __result.Append(MetadataDependencies.PluginAssembly).Distinct();
}

[HarmonyPatch(typeof(AttributeIndexer), nameof(AttributeIndexer.Observe))]
[HarmonyPatchCategory("Finish")]
internal static class AttributeIndexerObservePatch
{
    static void Postfix(AttributeIndexer __instance, Assembly assembly)
    {
        if (assembly != MetadataDependencies.PluginAssembly)
            return;

        foreach (Type component in MetadataDependencies.IndexedComponents)
        {
            __instance._sets.Add(typeof(WithLifetimeAdapterAttribute), component);
            __instance._sets.Add(typeof(IndexedComponentAttribute), component);
        }
        Console.WriteLine(
            "[LinuxCompat] Registered Linux engine components with the metadata indexer."
        );
    }
}

internal static class MetadataDependencies
{
    public static readonly Assembly PluginAssembly = typeof(MetadataDependencies).Assembly;

    /// <summary>
    /// The Linux engine components registered by GameAppPatch.AddPlatform. The DCS source
    /// generator normally bakes this index into module attributes; the Pulsar-compiled plugin
    /// cannot apply those attributes (the compiler references a conflicting generator copy of
    /// the attribute types), so the index entries are injected at observation time instead.
    /// </summary>
    public static readonly Type[] IndexedComponents =
    [
        typeof(LinuxMemoryEngineComponent),
        typeof(LinuxRenderEngineComponent),
        typeof(SdlInputComponent),
        typeof(LinuxSystemEngineComponent),
        typeof(LinuxWindowsEngineComponent),
    ];

    public static Type FindType(string name) =>
        Assembly.Load("VRage.Library").GetType(name, throwOnError: true)!;
}
