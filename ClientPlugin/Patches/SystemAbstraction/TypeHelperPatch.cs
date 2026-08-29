using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Keen.VRage.Library.Reflection.Advanced;

namespace LinuxCompat.Patches.SystemAbstraction;

[HarmonyPatch(typeof(TypeHelper), nameof(TypeHelper.GetValueTypeSize))]
[HarmonyPatchCategory("Finish")]
internal static class TypeHelperPatch
{
    private static readonly ConcurrentDictionary<Type, uint> Sizes = new();

    // The value type is known only at runtime, so constructing SizeOf<T> remains reflective.
    private static readonly MethodInfo SizeOfMethod = AccessTools.DeclaredMethod(
        typeof(TypeHelperPatch),
        nameof(SizeOf)
    )!;

    static bool Prefix(Type type, ref uint __result)
    {
        if (!type.IsValueType)
            return true;

        __result = Sizes.GetOrAdd(
            type,
            static valueType => (uint)SizeOfMethod.MakeGenericMethod(valueType).Invoke(null, null)!
        );
        return false;
    }

    private static uint SizeOf<T>() => (uint)Unsafe.SizeOf<T>();
}
