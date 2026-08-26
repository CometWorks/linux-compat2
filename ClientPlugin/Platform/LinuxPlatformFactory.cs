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
        string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        string appDataPath = customUserDataPath ?? Path.Combine(dataHome, Keen.VRage.Library.Utils.Singleton<VRageCore>.Instance.ApplicationName);
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

    public void RestartToReport() => Environment.Exit(-1);

    public void TerminateProcess(int exitCode) => Environment.Exit(exitCode);

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
