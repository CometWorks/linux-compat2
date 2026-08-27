using HarmonyLib;

namespace LinuxCompat.Patches.SystemAbstraction;

[HarmonyPatch(typeof(MemoryStream), nameof(Stream.CopyTo), typeof(Stream), typeof(int))]
[HarmonyPatchCategory("Finish")]
internal static class JsonSerializationPatch
{
    static void Prefix(Stream __instance)
    {
        if (
            __instance is MemoryStream { Position: 5 } memory
            && memory.TryGetBuffer(out ArraySegment<byte> buffer)
            && buffer.AsSpan().StartsWith("{\n  \""u8)
        )
            memory.Position = 4;
    }
}
