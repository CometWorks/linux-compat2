// ReSharper disable CheckNamespace

using System.Collections.Generic;
using HarmonyLib;
using LinuxCompat.Platform;
using LinuxCompat.Preloading;
using Mono.Cecil;

// IMPORTANT: MUST NOT USE A NAMESPACE, otherwise Pulsar won't find the Preloader class!

// ReSharper disable once UnusedType.Global
public static class Preloader
{
    // ReSharper disable once UnusedMember.Global
    public static IEnumerable<string> TargetDLLs =>
        ReadyToRunDisabled() ? ["VRage.Steam.dll"] : ReadyToRun.Dlls;

    // ReSharper disable once UnusedMember.Global
    public static void Patch(AssemblyDefinition asmDef)
    {
        if (asmDef.Name.Name == "VRage.Steam")
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
        ManagedAssemblyResolver.Install();
        LinuxNativeLibraryResolver.Install();
        new Harmony("LinuxCompat").PatchCategory("Finish");
        Console.WriteLine("[LinuxCompat] Installed all Linux compatibility patches.");
    }

    private static bool ReadyToRunDisabled() =>
        IsDisabled(Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"))
        || IsDisabled(Environment.GetEnvironmentVariable("COMPlus_ReadyToRun"));

    private static bool IsDisabled(string? value) =>
        value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
}
