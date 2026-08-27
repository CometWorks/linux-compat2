using System.Reflection;
using HarmonyLib;
using Keen.VRage.Core.Platform.CrashReporting;

namespace LinuxCompat.Patches;

/// <summary>
/// Disables crash and error reporting to Keen on Linux. Running the game on Linux is not
/// supported by them, so every report produced here is noise they cannot act on. The
/// uploads observed in practice were rejected by their endpoint anyway.
///
/// Reports are still written locally: the crash handler keeps filling
/// <c>Temp/CrashReports</c> and the game log records the exception, so they remain available
/// for local debugging.
/// </summary>
internal static class CrashReportingPatch
{
    public static void Install()
    {
        Harmony harmony = new("LinuxCompat.CrashReporting");

        // Forcing the setup to Disabled makes the handler's own switch clear
        // _processCrashReports, which gates report processing and uploading.
        MethodInfo initialize =
            AccessTools.DeclaredMethod(typeof(CrashHandler), "Initialize")
            ?? throw new MissingMethodException(typeof(CrashHandler).FullName, "Initialize");
        harmony.Patch(
            initialize,
            prefix: new HarmonyMethod(
                AccessTools.DeclaredMethod(typeof(CrashReportingPatch), nameof(InitializePrefix))!
            )
        );

        // Belt and braces: the upload paths decline directly, so a report is never sent even
        // if the game reaches them through a route that does not consult the setup.
        foreach (string name in new[] { "SendReport", "SendSuddenDeathReport" })
        {
            MethodInfo target =
                AccessTools.DeclaredMethod(typeof(CrashHandler), name)
                ?? throw new MissingMethodException(typeof(CrashHandler).FullName, name);
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    AccessTools.DeclaredMethod(typeof(CrashReportingPatch), nameof(SkipSendPrefix))!
                )
            );
        }
    }

    private static void InitializePrefix(ref CrashReportingSetup setup)
    {
        setup.Options = CrashReportingOptions.Disabled;
        Console.WriteLine(
            "[LinuxCompat] Crash reporting to the game's developer is disabled on Linux."
        );
    }

    /// <summary>
    /// Skips the upload and hands back a completed task, so callers that wait on the result
    /// (the terminating crash path waits up to ten minutes) continue immediately.
    /// </summary>
    private static bool SkipSendPrefix(ref Keen.VRage.Library.Threading.Task __result)
    {
        __result = System.Threading.Tasks.Task.CompletedTask;
        return false;
    }
}
