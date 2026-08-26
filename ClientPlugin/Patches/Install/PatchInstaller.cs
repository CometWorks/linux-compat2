using HarmonyLib;
using LinuxCompat.Platform;

namespace LinuxCompat.Patches.Install;

/// <summary>
/// Installs every LinuxCompat patch. Called from the Pulsar preloader Finish hook, which
/// runs in-process after plugin compilation and before the game's Program.Main, so all
/// patches are in place before any target method is JIT compiled.
/// </summary>
public static class PatchInstaller
{
    private static int _installed;

    public static void InstallAll()
    {
        if (!OperatingSystem.IsLinux() || Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        LinuxNativeLibraryResolver.Install();

        // Several installers resolve types by name, which only searches loaded assemblies.
        InstallTools.LoadAssembly("VRage.Library");
        InstallTools.LoadAssembly("VRage.Core");

        // Patches owning their own installation (several defer until their target assembly loads).
        AppTimerPatch.Install();
        JsonSerializationPatch.Install();
        TypeHelperPatch.Install();
        NativeMemoryPatch.Install();
        MetadataDependenciesPatch.Install();
        NativeFileSystemPathCasePatch.Install();
        InputExtensionsPatch.Install();
        MainRenderTargetPatch.Install();
        PersistentDrawBatchPatch.Install();
        UIEngineComponentPatch.Install();
        OctreeInfoDisposePatch.Install();
        AnimationTimingPatch.Install();
        RenderTimingPatch.Install();
        BannerFetchServicePatch.Install();
        BatteryStatusPatch.Install();
        RenderVendorApiPatch.Install();

        // Patches against the shipped game executable and renderer.
        Harmony harmony = new("LinuxCompat");
        GameExePatches.Install(harmony);
        Render12Patches.Install(harmony);

        // The game log is not initialized yet at preloader time.
        Console.WriteLine("[LinuxCompat] Installed all Linux compatibility patches.");
    }
}
