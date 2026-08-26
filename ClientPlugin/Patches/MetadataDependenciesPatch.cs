using System.Reflection;
using HarmonyLib;
using Keen.VRage.Library.Collections.Readers;
using Keen.VRage.Library.Reflection;

namespace LinuxCompat.Patches;

internal static class MetadataDependenciesPatch
{
    public static void Install()
    {
        Type helper = AccessTools.TypeByName("Keen.VRage.Library.Reflection.MetadataHelper")
            ?? throw new TypeLoadException("VRage metadata helpers were not found.");
        MethodInfo target = AccessTools.DeclaredMethod(helper, "GetAssembliesWithMetadataDependencies")
            ?? throw new MissingMethodException(helper.FullName, "GetAssembliesWithMetadataDependencies");
        MethodInfo prefix = AccessTools.DeclaredMethod(typeof(MetadataDependenciesPatch), nameof(Prefix))!;
        new Harmony("LinuxCompat.MetadataDependencies").Patch(target, prefix: new HarmonyMethod(prefix));
    }

    private static bool Prefix(IEnumerable<Assembly> assemblies, ref HashSetReader<Assembly> __result)
    {
        HashSet<Assembly> result = [];
        result.Add(typeof(MetadataDependenciesPatch).Assembly);
        foreach (Assembly assembly in assemblies)
        {
            result.Add(assembly);
            MetadataDependenciesAttribute? dependencies = assembly.GetCustomAttribute<MetadataDependenciesAttribute>();
            if (dependencies == null)
                continue;

            foreach (string assemblyName in dependencies.AssemblyNames)
            {
                if (!assemblyName.StartsWith("VRage.Platform.Windows,", StringComparison.Ordinal))
                    result.Add(Assembly.Load(assemblyName));
            }
        }

        __result = new HashSetReader<Assembly>(result);
        return false;
    }
}
