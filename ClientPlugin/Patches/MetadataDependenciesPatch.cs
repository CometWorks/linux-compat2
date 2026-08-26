using System.Reflection;
using HarmonyLib;
using LinuxCompat.Platform;

namespace LinuxCompat.Patches;

/// <summary>
/// Keeps VRage.Platform.Windows out of the metadata dependency expansion on Linux and adds
/// the plugin assembly, whose Linux engine components carry DCS annotations, to the scanned
/// set. Implemented without referencing HashSetReader or MetadataDependenciesAttribute at
/// compile time: the Pulsar plugin compiler transitively references VRage.Library.Generator,
/// which defines conflicting copies of both types.
/// </summary>
internal static class MetadataDependenciesPatch
{
    public static void Install()
    {
        Harmony harmony = new("LinuxCompat.MetadataDependencies");

        Type helper = AccessTools.TypeByName("Keen.VRage.Library.Reflection.MetadataHelper")
            ?? throw new TypeLoadException("VRage metadata helpers were not found.");
        MethodInfo target = AccessTools.DeclaredMethod(helper, "GetAssembliesWithMetadataDependencies")
            ?? throw new MissingMethodException(helper.FullName, "GetAssembliesWithMetadataDependencies");
        harmony.Patch(target, prefix: new HarmonyMethod(
            AccessTools.DeclaredMethod(typeof(MetadataDependenciesPatch), nameof(AddPluginAssemblyPrefix))!));

        Type attribute = AccessTools.TypeByName("Keen.VRage.Library.Reflection.MetadataDependenciesAttribute")
            ?? throw new TypeLoadException("MetadataDependenciesAttribute was not found.");
        ConstructorInfo constructor = AccessTools.Constructor(attribute, [typeof(string[])])
            ?? throw new MissingMethodException(attribute.FullName, ".ctor(string[])");
        harmony.Patch(constructor, prefix: new HarmonyMethod(
            AccessTools.DeclaredMethod(typeof(MetadataDependenciesPatch), nameof(FilterDependenciesPrefix))!));

        MethodInfo filter = AccessTools.DeclaredMethod(helper, "FilterAssembliesWithMetadata")
            ?? throw new MissingMethodException(helper.FullName, "FilterAssembliesWithMetadata");
        harmony.Patch(filter, postfix: new HarmonyMethod(
            AccessTools.DeclaredMethod(typeof(MetadataDependenciesPatch), nameof(IncludePluginAssemblyPostfix))!));

        Type attributeIndexer = AccessTools.TypeByName("Keen.VRage.Library.Reflection.AttributeIndexer")
            ?? throw new TypeLoadException("AttributeIndexer was not found.");
        _attributeIndexerSets = AccessTools.DeclaredField(attributeIndexer, "_sets")
            ?? throw new MissingFieldException(attributeIndexer.FullName, "_sets");
        _attributeIndexerSetsAdd = AccessTools.Method(_attributeIndexerSets.FieldType, "Add", [typeof(Type), typeof(Type)])
            ?? throw new MissingMethodException(_attributeIndexerSets.FieldType.FullName, "Add");
        harmony.Patch(AccessTools.DeclaredMethod(attributeIndexer, "Observe", [typeof(Assembly)])
                ?? throw new MissingMethodException(attributeIndexer.FullName, "Observe"),
            postfix: new HarmonyMethod(
                AccessTools.DeclaredMethod(typeof(MetadataDependenciesPatch), nameof(ObservePostfix))!));
    }

    /// <summary>
    /// The Linux engine components registered by GameAppPatch.AddPlatform. The DCS source
    /// generator normally bakes this index into module attributes; the Pulsar-compiled plugin
    /// cannot apply those attributes (the compiler references a conflicting generator copy of
    /// the attribute types), so the index entries are injected at observation time instead.
    /// </summary>
    private static readonly Type[] IndexedComponents =
    [
        typeof(LinuxMemoryEngineComponent),
        typeof(LinuxRenderEngineComponent),
        typeof(SdlInputComponent),
        typeof(LinuxSystemEngineComponent),
        typeof(LinuxWindowsEngineComponent),
    ];

    private static readonly string[] IndexedAttributeTypeNames =
    [
        "Keen.VRage.Library.Threading.WithLifetimeAdapterAttribute",
        "Keen.VRage.DCS.Internal.IndexedComponentAttribute",
    ];

    private static FieldInfo _attributeIndexerSets = null!;
    private static MethodInfo _attributeIndexerSetsAdd = null!;

    private static void IncludePluginAssemblyPostfix(ref IEnumerable<Assembly> __result)
    {
        if (OperatingSystem.IsLinux())
            __result = __result.Append(typeof(MetadataDependenciesPatch).Assembly).Distinct();
    }

    private static void ObservePostfix(object __instance, Assembly assembly)
    {
        if (!OperatingSystem.IsLinux() || assembly != typeof(MetadataDependenciesPatch).Assembly)
            return;

        object sets = _attributeIndexerSets.GetValue(__instance)!;
        foreach (string attributeTypeName in IndexedAttributeTypeNames)
        {
            Type attributeType = AccessTools.TypeByName(attributeTypeName)
                ?? throw new TypeLoadException($"{attributeTypeName} was not found.");
            foreach (Type component in IndexedComponents)
                _attributeIndexerSetsAdd.Invoke(sets, [attributeType, component]);
        }
        Console.WriteLine("[LinuxCompat] Registered Linux engine components with the metadata indexer.");
    }

    private static void AddPluginAssemblyPrefix(ref IEnumerable<Assembly> __0)
    {
        if (OperatingSystem.IsLinux())
            __0 = __0.Append(typeof(MetadataDependenciesPatch).Assembly).Distinct();
    }

    /// <summary>Attribute instances are materialized through this constructor on every
    /// GetCustomAttribute call, so filtering here keeps the expansion Windows-free.</summary>
    private static void FilterDependenciesPrefix(ref string[] assemblyNames)
    {
        if (OperatingSystem.IsLinux()
            && assemblyNames.Any(name => name.StartsWith("VRage.Platform.Windows,", StringComparison.Ordinal)))
        {
            assemblyNames = assemblyNames
                .Where(name => !name.StartsWith("VRage.Platform.Windows,", StringComparison.Ordinal))
                .ToArray();
        }
    }
}
