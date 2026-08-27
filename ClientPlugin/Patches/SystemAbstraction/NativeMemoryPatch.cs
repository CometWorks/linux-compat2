using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using HarmonyLib;
using GameNativeMemory = Keen.VRage.Library.Memory.NativeMemory;

namespace LinuxCompat.Patches.SystemAbstraction;

[HarmonyPatch(typeof(GameNativeMemory), nameof(GameNativeMemory.TotalAllocated), MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
internal static class NativeMemoryTotalAllocatedPatch
{
    static bool Prefix(ref nuint __result)
    {
        __result = NativeMemoryAllocations.TotalAllocated;
        return false;
    }
}

[HarmonyPatch(typeof(GameNativeMemory), nameof(GameNativeMemory.Allocate))]
[HarmonyPatchCategory("Finish")]
internal static class NativeMemoryAllocatePatch
{
    static unsafe bool Prefix(nuint size, nuint alignment, ref nint __result)
    {
        nuint actualSize = checked((size + alignment - 1) / alignment * alignment);
        __result = (nint)NativeMemory.AlignedAlloc(actualSize, alignment);
        NativeMemoryAllocations.Add(__result, size);
        return false;
    }
}

[HarmonyPatch(typeof(GameNativeMemory), nameof(GameNativeMemory.GetAllocationSize))]
[HarmonyPatchCategory("Finish")]
internal static class NativeMemoryGetAllocationSizePatch
{
    static bool Prefix(nint ptr, ref nuint __result)
    {
        __result = NativeMemoryAllocations.GetSize(ptr);
        return false;
    }
}

[HarmonyPatch(typeof(GameNativeMemory), nameof(GameNativeMemory.Free))]
[HarmonyPatchCategory("Finish")]
internal static class NativeMemoryFreePatch
{
    static unsafe bool Prefix(nint ptr)
    {
        NativeMemoryAllocations.Remove(ptr);
        NativeMemory.AlignedFree((void*)ptr);
        return false;
    }
}

internal static class NativeMemoryAllocations
{
    private static readonly ConcurrentDictionary<nint, nuint> Allocations = new();
    private static long _totalAllocated;

    public static nuint TotalAllocated => (nuint)Interlocked.Read(ref _totalAllocated);

    public static void Add(nint ptr, nuint size)
    {
        if (ptr == 0)
            return;
        Allocations[ptr] = size;
        Interlocked.Add(ref _totalAllocated, checked((long)size));
    }

    public static nuint GetSize(nint ptr)
    {
        Allocations.TryGetValue(ptr, out nuint size);
        return size;
    }

    public static void Remove(nint ptr)
    {
        if (Allocations.TryRemove(ptr, out nuint size))
            Interlocked.Add(ref _totalAllocated, -checked((long)size));
    }
}
