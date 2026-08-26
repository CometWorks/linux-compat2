using Keen.VRage.Core;
using Keen.VRage.Core.Platform;
using Keen.VRage.Core.Platform.CrashReporting;
using Keen.VRage.Library.Filesystem;
using Keen.VRage.Library.Mathematics;

namespace LinuxCompat.Platform;

internal sealed class LinuxPlatformFactory : IPlatformFactory
{
    public VRagePlatformCore CreateCore(string? customUserDataPath, string[] args)
    {
        string appDataPath = LinuxDataFolder.Resolve(customUserDataPath);
        Directory.CreateDirectory(appDataPath);
        Directory.CreateDirectory(Path.Combine(appDataPath, "Temp"));

        return new VRagePlatformCore
        {
            AppDataPath = appDataPath,
            CrashReporter = new LinuxCrashReporter(),
            Http = new LinuxHttpClient()
        };
    }

    public INativeCrashReporter CreateNativeCrashReporter() => LinuxNativeCrashReporter.Instance;

    /// <summary>
    /// Windows restarts the game to upload the pending crash report and then terminates. Reports
    /// to the game's developer are disabled on Linux (see <c>CrashReportingPatch</c>), so there
    /// is nothing to restart for and only the termination is left.
    /// </summary>
    public void RestartToReport() => LinuxProcessTermination.Terminate(-1);

    public void TerminateProcess(int exitCode) => LinuxProcessTermination.Terminate(exitCode);

    /// <summary>
    /// The Linux splash screen is a borderless SDL image window with no text layer, so the
    /// loading font, text offset, and progress notifier the Windows form uses are ignored.
    /// </summary>
    public void TryCreateSplashScreen(string splashScreen, string splashScreenIcon, string splashScreenFont,
        Vector2I splashScreenLoadingOffset, ILoadingProgressNotifier progressNotifier)
    {
        if (!File.Exists(splashScreen))
            splashScreen = Path.Combine(Environment.CurrentDirectory, Path.GetFileName(splashScreen));
        if (!File.Exists(splashScreenIcon))
            splashScreenIcon = Path.Combine(Environment.CurrentDirectory, Path.GetFileName(splashScreenIcon));
        SdlPlatformWindow.SetWindowIcon(splashScreenIcon);
        LinuxSplashScreen.Show(splashScreen, splashScreenIcon);
    }

    private sealed class LinuxNativeCrashReporter : INativeCrashReporter
    {
        public static readonly LinuxNativeCrashReporter Instance = new();

        public void TrackNativeCrashes(string appName, ref string[] args)
        {
        }
    }

    private sealed class LinuxCrashReporter : ICrashReporter
    {
        public void WriteMiniDump(string path, MiniDump.Options flags, nint exceptionPointers)
        {
        }

        public void SetNativeExceptionHandler(Action<nint> handler)
        {
        }

        public bool CheckDevelopmentParentProcess() => false;

        public MessageBoxResult MessageBox(string text, string caption, MessageBoxOptions options)
        {
            Console.Error.WriteLine($"{caption}: {text}");
            return MessageBoxResult.Ok;
        }

        public void OkMessageBox(string text, string caption) => MessageBox(text, caption, MessageBoxOptions.OkOnly);

        public bool IsVirtualProcess(out string virtualizerName)
        {
            virtualizerName = null!;
            return false;
        }

        public bool TryGetProcessDriveType(out DiskInfo driveType)
        {
            driveType = DiskInfo.Default;
            return false;
        }

        public DiskInfo GetDriveTypeForPath(string path) => DiskInfo.Default;

        public string? TryGetPlatformCrashInfo(DateTime from, DateTime to) => null;
    }
}
