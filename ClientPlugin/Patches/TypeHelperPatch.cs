using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LinuxCompat.Patches;

internal static class TypeHelperPatch
{
    private static readonly ConcurrentDictionary<Type, uint> Sizes = new();
    private static readonly MethodInfo SizeOfMethod = AccessTools.DeclaredMethod(typeof(TypeHelperPatch), nameof(SizeOf))!;

    public static void Install()
    {
        Type typeHelper = AccessTools.TypeByName("Keen.VRage.Library.Reflection.Advanced.TypeHelper")
            ?? throw new TypeLoadException("VRage type helper was not found.");
        MethodInfo target = AccessTools.DeclaredMethod(typeHelper, "GetValueTypeSize", [typeof(Type)])
            ?? throw new MissingMethodException(typeHelper.FullName, "GetValueTypeSize");
        new Harmony("LinuxCompat.TypeHelper").Patch(
            target,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(TypeHelperPatch), nameof(Prefix))!));
    }

    private static bool Prefix(Type type, ref uint __result)
    {
        if (!OperatingSystem.IsLinux() || !type.IsValueType)
            return true;

        __result = Sizes.GetOrAdd(type, static valueType =>
            (uint)SizeOfMethod.MakeGenericMethod(valueType).Invoke(null, null)!);
        return false;
    }

    private static uint SizeOf<T>() => (uint)Unsafe.SizeOf<T>();
}
