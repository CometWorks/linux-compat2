namespace LinuxCompat.Patches;

public static class FramePacerPatch
{
    public static bool Prefix(ref float? __result)
    {
        __result = null;
        return false;
    }
}
