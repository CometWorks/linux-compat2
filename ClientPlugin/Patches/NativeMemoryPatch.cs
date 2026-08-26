using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace LinuxCompat.Patches;

internal static class NativeMemoryPatch
{
    private static readonly ConcurrentDictionary<nint, nuint> Allocations = new();
    private static long _totalAllocated;

    public static void Install()
    {
        Type nativeMemory = AccessTools.TypeByName("Keen.VRage.Library.Memory.NativeMemory")
            ?? throw new TypeLoadException("VRage native memory wrapper was not found.");
        Harmony harmony = new("LinuxCompat.NativeMemory");
        Patch(harmony, AccessTools.PropertyGetter(nativeMemory, "TotalAllocated"), nameof(GetTotalAllocatedPrefix));
        Patch(harmony, AccessTools.DeclaredMethod(nativeMemory, "Allocate"), nameof(AllocatePrefix));
        Patch(harmony, AccessTools.DeclaredMethod(nativeMemory, "GetAllocationSize"), nameof(GetAllocationSizePrefix));
        Patch(harmony, AccessTools.DeclaredMethod(nativeMemory, "Free"), nameof(FreePrefix));
    }

    private static void Patch(Harmony harmony, MethodInfo? target, string prefixName)
    {
        if (target == null)
            throw new MissingMethodException("Keen.VRage.Library.Memory.NativeMemory", prefixName);
        harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(NativeMemoryPatch), prefixName)));
    }

    private static bool GetTotalAllocatedPrefix(ref nuint __result)
    {
        __result = (nuint)Interlocked.Read(ref _totalAllocated);
        return false;
    }

    private static unsafe bool AllocatePrefix(nuint size, nuint alignment, ref nint __result)
    {
        nuint actualSize = checked((size + alignment - 1) / alignment * alignment);
        __result = (nint)NativeMemory.AlignedAlloc(actualSize, alignment);
        if (__result != 0)
        {
            Allocations[__result] = size;
            Interlocked.Add(ref _totalAllocated, checked((long)size));
        }
        return false;
    }

    private static bool GetAllocationSizePrefix(nint ptr, ref nuint __result)
    {
        Allocations.TryGetValue(ptr, out __result);
        return false;
    }

    private static unsafe bool FreePrefix(nint ptr)
    {
        if (Allocations.TryRemove(ptr, out nuint size))
            Interlocked.Add(ref _totalAllocated, -checked((long)size));
        NativeMemory.AlignedFree((void*)ptr);
        return false;
    }
}
