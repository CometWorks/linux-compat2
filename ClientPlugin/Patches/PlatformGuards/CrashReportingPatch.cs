using HarmonyLib;
using Keen.VRage.Core.Platform.CrashReporting;

namespace LinuxCompat.Patches.PlatformGuards;

/// <summary>
/// Disables crash and error reporting to Keen on Linux. Running the game on Linux is not
/// supported by them, so every report produced here is noise they cannot act on. The
/// uploads observed in practice were rejected by their endpoint anyway.
///
/// Reports are still written locally: the crash handler keeps filling
/// <c>Temp/CrashReports</c> and the game log records the exception, so they remain available
/// for local debugging.
/// </summary>
[HarmonyPatch(typeof(CrashHandler), nameof(CrashHandler.Initialize))]
[HarmonyPatchCategory("Finish")]
internal static class CrashHandlerInitializePatch
{
    // Forcing the setup to Disabled makes the handler's own switch clear
    // _processCrashReports, which gates report processing and uploading.
    static void Prefix(ref CrashReportingSetup setup)
    {
        setup.Options = CrashReportingOptions.Disabled;
        Console.WriteLine(
            "[LinuxCompat] Crash reporting to the game's developer is disabled on Linux."
        );
    }
}

// The upload paths decline directly, so a report is never sent even if the game reaches
// them through a route that does not consult the setup.
[HarmonyPatch(typeof(CrashHandler), nameof(CrashHandler.SendReport))]
[HarmonyPatchCategory("Finish")]
internal static class CrashHandlerSendReportPatch
{
    static bool Prefix(ref Keen.VRage.Library.Threading.Task __result) =>
        CrashReporting.SkipSend(ref __result);
}

[HarmonyPatch(typeof(CrashHandler), nameof(CrashHandler.SendSuddenDeathReport))]
[HarmonyPatchCategory("Finish")]
internal static class CrashHandlerSendSuddenDeathReportPatch
{
    static bool Prefix(ref Keen.VRage.Library.Threading.Task __result) =>
        CrashReporting.SkipSend(ref __result);
}

internal static class CrashReporting
{
    /// <summary>Hands back a completed task so callers waiting on the upload, including the
    /// terminating crash path's ten-minute wait, continue immediately.</summary>
    public static bool SkipSend(ref Keen.VRage.Library.Threading.Task result)
    {
        result = Task.CompletedTask;
        return false;
    }
}
