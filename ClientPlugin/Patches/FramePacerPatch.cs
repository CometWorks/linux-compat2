namespace LinuxCompat.Patches;

public static class FramePacerPatch
{
    public static bool Prefix(ref float? __result)
    {
        if (!OperatingSystem.IsLinux())
            return true;

        __result = null;
        return false;
    }
}
