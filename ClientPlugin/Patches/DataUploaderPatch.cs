namespace LinuxCompat.Patches;

public static class DataUploaderPatch
{
    public const int DefaultBlockSize = 256 * 1024 * 1024;
    public const int CpuRenderingBlockSize = 32 * 1024 * 1024;

    public static int GetBlockSize(int originalSize) =>
        OperatingSystem.IsLinux() && Platform.LinuxNativeLibraryResolver.IsEnabled("SE2_CPU_RENDERING")
            ? CpuRenderingBlockSize
            : originalSize;
}
