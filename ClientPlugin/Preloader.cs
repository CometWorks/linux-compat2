// ReSharper disable CheckNamespace

using System.Collections.Generic;
using LinuxCompat.Patches.Install;
using Mono.Cecil;

// IMPORTANT: MUST NOT USE A NAMESPACE, otherwise Pulsar won't find the Preloader class!

// ReSharper disable once UnusedType.Global
public static class Preloader
{
    /// <summary>
    /// VRage.Steam binds to Pulsar's Linux Steamworks wrapper at run time, and two of its
    /// methods reference members the wrapper does not expose; those cannot be patched with
    /// Harmony (their tokens no longer resolve), so the assembly is rewritten with Cecil
    /// before it loads. Everything else is patched with Harmony from Finish.
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    public static IEnumerable<string> TargetDLLs =>
        System.OperatingSystem.IsLinux() ? ["VRage.Steam.dll"] : [];

    // ReSharper disable once UnusedMember.Global
    public static void Patch(AssemblyDefinition asmDef)
    {
        SteamPrepatch.Apply(asmDef);
    }

    /// <summary>
    /// Runs in-process after preloader patching and before the game's Program.Main, which
    /// makes it the right moment to install every Harmony patch: nothing in the game has
    /// been JIT compiled yet.
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    public static void Finish()
    {
        PatchInstaller.InstallAll();
    }
}
