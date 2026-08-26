using System.Runtime.InteropServices;
using System.Text;

namespace LinuxCompat.Platform;

/// <summary>
/// Process-wide environment settings that must be in place before the CLR and Steam
/// initialize, applied from the Pulsar preloader's <c>Initialize</c> hook.
///
/// Two of them cannot be applied to an already-running process:
///
/// <list type="bullet">
/// <item><c>DOTNET_ReadyToRun=0</c> is read by the runtime during startup, long before any
/// managed plugin code runs, and only from the environment (a <c>runtimeconfig.json</c>
/// property is ignored). Without it every game assembly fails to load with
/// <c>BadImageFormatException</c>, because the shipped assemblies are win-x64 ReadyToRun
/// images whose precompiled native code is unusable on Linux.</item>
/// <item><c>BREAKPAD_DUMP_LOCATION</c> is read by Valve's <c>crashhandler.so</c>, which
/// Steam installs during <c>SteamAPI_Init</c> — and Pulsar initializes Steam before it
/// loads plugins.</item>
/// </list>
///
/// So when the variables are missing the process re-executes itself with them set. This
/// replaces the process image in place (no child process): Pulsar's single-instance mutex
/// is left abandoned rather than held, which its own startup check already treats as
/// ownership, and Steam plus the CLR simply initialize once more in the new image.
/// </summary>
internal static class LinuxProcessEnvironment
{
    private const string ReadyToRunVariable = "DOTNET_ReadyToRun";
    private const string BreakpadVariable = "BREAKPAD_DUMP_LOCATION";
    private const string ReExecMarkerVariable = "LINUXCOMPAT_REEXEC";
    private const string OptOutVariable = "SE2_NO_REEXEC";

    public static void Apply()
    {
        if (!OperatingSystem.IsLinux())
            return;

        string dumpDirectory = GetMiniDumpDirectory();
        TryCreateDirectory(dumpDirectory);
        CollectSteamMiniDumps(dumpDirectory);

        bool readyToRunMissing = !IsDisabled(Environment.GetEnvironmentVariable(ReadyToRunVariable));
        bool breakpadMissing = Environment.GetEnvironmentVariable(BreakpadVariable) != dumpDirectory;

        // Best effort for the current process: harmless when it changes nothing, and it is
        // the only option once a re-exec has already happened or was opted out of.
        if (breakpadMissing)
            SetEnvironmentVariable(BreakpadVariable, dumpDirectory);

        if (!readyToRunMissing && !breakpadMissing)
            return;

        if (Environment.GetEnvironmentVariable(ReExecMarkerVariable) == "1")
        {
            // Already re-executed once; never loop.
            if (readyToRunMissing)
                Console.Error.WriteLine(
                    $"[LinuxCompat] WARNING: {ReadyToRunVariable}=0 did not survive the restart. " +
                    "Game assemblies will fail to load. Set it in the launch environment.");
            return;
        }

        if (IsEnabled(Environment.GetEnvironmentVariable(OptOutVariable)))
        {
            if (readyToRunMissing)
                Console.Error.WriteLine(
                    $"[LinuxCompat] WARNING: {ReadyToRunVariable}=0 is not set and {OptOutVariable} " +
                    "disables the restart. Game assemblies will fail to load.");
            return;
        }

        ReExec(dumpDirectory);
    }

    /// <summary>
    /// Replaces the current process image with the same command line, with the required
    /// variables set. Returns only if the restart could not be performed.
    /// </summary>
    private static void ReExec(string dumpDirectory)
    {
        string[] arguments;
        try
        {
            arguments = ReadCommandLine();
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"[LinuxCompat] WARNING: cannot read /proc/self/cmdline: {exception.Message}");
            return;
        }
        if (arguments.Length == 0)
            return;

        SetEnvironmentVariable(ReadyToRunVariable, "0");
        SetEnvironmentVariable(BreakpadVariable, dumpDirectory);
        SetEnvironmentVariable(ReExecMarkerVariable, "1");

        Console.WriteLine(
            $"[LinuxCompat] Restarting with {ReadyToRunVariable}=0 (required to load the game's " +
            $"ReadyToRun assemblies) and minidumps redirected to {dumpDirectory}.");
        Console.Out.Flush();

        // execv() needs a null-terminated argument vector.
        string?[] argv = [.. arguments, null];
        execv("/proc/self/exe", argv!);

        // Only reached when execv failed.
        Console.Error.WriteLine(
            $"[LinuxCompat] WARNING: restart failed (errno {Marshal.GetLastWin32Error()}). " +
            $"Launch the game with {ReadyToRunVariable}=0 set.");
    }

    private static string[] ReadCommandLine()
    {
        byte[] raw = File.ReadAllBytes("/proc/self/cmdline");
        return Encoding.UTF8.GetString(raw)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Moves the minidumps Steam's crash handler left in <c>/tmp/dumps</c> into the game's own
    /// minidump directory.
    ///
    /// Valve's <c>crashhandler.so</c>, which Steam installs during <c>SteamAPI_Init</c>, writes
    /// its dumps to a hard-wired <c>/tmp/dumps</c>; it reads <c>BREAKPAD_DUMP_LOCATION</c> but
    /// does not use it for that folder, and the dump is written by a forked child while the
    /// game is dying, so the running process cannot move it in time. Collecting on the next
    /// start keeps the dumps with the game's other diagnostics.
    ///
    /// Only dumps newer than the marker written by the previous start are taken, so unrelated
    /// dumps that predate this game's session are left alone. Another Steam game crashing
    /// during the same window would be swept up too, which is why the file is moved rather
    /// than deleted. Empty files are skipped: they are either still being written or one of
    /// the placeholders Steam leaves behind.
    ///
    /// The whole sweep is best effort. It runs from the preloader, before the game starts, so
    /// no failure here may propagate; and every dump is handled on its own, because the crash
    /// handler and every other Steam game keep changing this directory while it runs.
    /// </summary>
    private static void CollectSteamMiniDumps(string dumpDirectory)
    {
        const string steamDumpDirectory = "/tmp/dumps";
        try
        {
            DateTime since = StampSession(dumpDirectory);

            if (!Directory.Exists(steamDumpDirectory))
                return;

            int moved = 0;
            foreach (string dump in EnumerateDumps(steamDumpDirectory))
            {
                if (TryTakeMiniDump(dump, dumpDirectory, since))
                    moved++;
            }

            if (moved != 0)
                Console.WriteLine($"[LinuxCompat] Moved {moved} Steam minidump(s) from {steamDumpDirectory} to {dumpDirectory}.");
        }
        catch (Exception exception)
        {
            // This runs from the preloader, so nothing here may reach the game's startup path.
            Warn($"cannot collect Steam minidumps: {exception.Message}");
        }
    }

    /// <summary>
    /// Returns the time the previous start was stamped, and stamps this one. Both halves are
    /// best effort: a marker that cannot be read means "take nothing", which is the safe
    /// direction, and a marker that cannot be written only costs the next start its dumps.
    /// </summary>
    private static DateTime StampSession(string dumpDirectory)
    {
        string marker = Path.Combine(dumpDirectory, ".last-session");
        DateTime since = DateTime.UtcNow;

        try
        {
            FileInfo info = new(marker);
            if (info.Exists)
                since = info.LastWriteTimeUtc;
        }
        catch (Exception exception)
        {
            Warn($"cannot read {marker}: {exception.Message}");
        }

        try
        {
            File.WriteAllText(marker, string.Empty);
        }
        catch (Exception exception)
        {
            Warn($"cannot update {marker}: {exception.Message}");
        }

        return since;
    }

    /// <summary>
    /// The dump files currently in <paramref name="directory"/>. Steam's <c>/tmp/dumps</c> is
    /// shared with every other game and written to by a crash handler that may still be
    /// running, so entries can vanish or be unreadable mid-walk; a failed listing yields
    /// whatever it already produced instead of losing the whole sweep.
    /// </summary>
    private static IEnumerable<string> EnumerateDumps(string directory)
    {
        EnumerationOptions options = new() { IgnoreInaccessible = true };
        IEnumerator<string> dumps;
        try
        {
            dumps = Directory.EnumerateFiles(directory, "*.dmp", options).GetEnumerator();
        }
        catch (Exception exception)
        {
            Warn($"cannot list {directory}: {exception.Message}");
            yield break;
        }

        using (dumps)
        {
            while (true)
            {
                try
                {
                    if (!dumps.MoveNext())
                        yield break;
                }
                catch (Exception exception)
                {
                    Warn($"stopped listing {directory}: {exception.Message}");
                    yield break;
                }

                yield return dumps.Current;
            }
        }
    }

    /// <summary>
    /// Moves a single dump if it belongs to the session that just ended. Failure is the
    /// normal case rather than an error here: the crash handler's forked child creates,
    /// truncates and removes files while this runs, and dumps left by another user in the
    /// shared directory are not ours to touch. Each dump therefore fails on its own, without
    /// aborting the ones after it and without logging noise for a directory full of files
    /// that were never ours.
    /// </summary>
    private static bool TryTakeMiniDump(string dump, string dumpDirectory, DateTime since)
    {
        try
        {
            FileInfo info = new(dump);

            // Gone since the listing, or an empty file — either still being written by the
            // crash handler or a placeholder such as the assert_*_4.dmp Steam leaves behind.
            if (!info.Exists || info.Length == 0)
                return false;

            if (info.LastWriteTimeUtc < since)
                return false;

            File.Move(dump, Path.Combine(dumpDirectory, info.Name), overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The game's minidump directory, matching <c>CrashHandler.GetMiniDumpsPath()</c>. It has
    /// to be computed here because the engine's own data path is not established yet, which is
    /// why the data folder itself comes from <see cref="LinuxDataFolder"/> rather than from
    /// <c>VRagePlatformCore.AppDataPath</c>.
    /// </summary>
    public static string GetMiniDumpDirectory() =>
        Path.Combine(LinuxDataFolder.Resolve(), "Temp", "MiniDumps");

    private static void TryCreateDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception exception)
        {
            Warn($"cannot create {directory}: {exception.Message}");
        }
    }

    /// <summary>
    /// Reports a non-fatal problem. Swallows even a failing console so that diagnostics can
    /// never be the reason the game does not start.
    /// </summary>
    private static void Warn(string message)
    {
        try
        {
            Console.Error.WriteLine($"[LinuxCompat] WARNING: {message}");
        }
        catch (Exception)
        {
        }
    }

    private static bool IsDisabled(string? value) => value is "0" or "false" or "False";

    private static bool IsEnabled(string? value) => value is "1" or "true" or "True";

    private static void SetEnvironmentVariable(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (setenv(name, value, overwrite: 1) != 0)
            throw new InvalidOperationException($"Failed to set native environment variable {name}.");
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    [DllImport("libc", SetLastError = true)]
    private static extern int execv(string path, string[] argv);
}
