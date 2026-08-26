using HarmonyLib;

namespace LinuxCompat.Patches;

public static class JsonSerializationPatch
{
    public static void Install()
    {
        new Harmony("LinuxCompat.JsonSerialization").Patch(
            AccessTools.DeclaredMethod(typeof(MemoryStream), nameof(Stream.CopyTo), [typeof(Stream), typeof(int)])!,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(JsonSerializationPatch), nameof(Prefix))!));
    }

    public static void AdjustArchiveStreamPosition(Stream source)
    {
        if (OperatingSystem.IsLinux()
            && source is MemoryStream { Position: 5 } memory
            && memory.TryGetBuffer(out ArraySegment<byte> buffer)
            && buffer.AsSpan().StartsWith("{\n  \""u8))
            memory.Position = 4;
    }

    private static void Prefix(Stream __instance) => AdjustArchiveStreamPosition(__instance);
}
