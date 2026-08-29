using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using Keen.VRage.Core;
using Keen.VRage.Core.EngineComponents;
using Keen.VRage.Core.Platform;
using Keen.VRage.Core.Platform.CrashReporting;
using Keen.VRage.Core.Render;
using Keen.VRage.DCS.Annotations;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Filesystem;
using Keen.VRage.Library.Localization;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Library.Utils;
using VrTask = Keen.VRage.Library.Threading.Task;

namespace LinuxCompat.Platform;

[DefaultTag("IPlatformMemory")]
internal sealed class LinuxMemoryEngineComponent : EngineComponent, IPlatformMemory
{
    public bool HasVirtualMemory => true;
    public ulong TotalPhysicalMemory =>
        (ulong)Math.Max(0, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
    public long ProcessMemory => Environment.WorkingSet;
    public bool IsAllocationProfilingReady => true;
    public IPlatformMemory.AvailableMemoryState MemoryState =>
        IPlatformMemory.AvailableMemoryState.Normal;

    public ulong GetThreadAllocationStamp() => (ulong)GC.GetAllocatedBytesForCurrentThread();

    public ulong GetGlobalAllocationsStamp() => (ulong)GC.GetTotalAllocatedBytes(precise: false);

    /// <summary>
    /// Linux equivalent of the Win32 <c>GlobalMemoryStatusEx</c> page-file report: the commit
    /// limit and the still uncommitted remainder come from <c>/proc/meminfo</c>.
    /// </summary>
    public bool TryGetCommitInfo(out long availableCommitBytes, out long commitLimitBytes)
    {
        availableCommitBytes = 0L;
        commitLimitBytes = 0L;
        long committedBytes = 0L;

        try
        {
            foreach (string line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("CommitLimit:", StringComparison.Ordinal))
                    commitLimitBytes = ReadMeminfoBytes(line);
                else if (line.StartsWith("Committed_AS:", StringComparison.Ordinal))
                    committedBytes = ReadMeminfoBytes(line);
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (commitLimitBytes <= 0)
            return false;

        availableCommitBytes = Math.Max(0L, commitLimitBytes - committedBytes);
        return true;
    }

    private static long ReadMeminfoBytes(string line)
    {
        ReadOnlySpan<char> value = line.AsSpan(line.IndexOf(':') + 1).Trim();
        int unit = value.IndexOf(' ');
        if (unit > 0)
            value = value[..unit];
        return long.TryParse(value, out long kilobytes) ? kilobytes * 1024L : 0L;
    }
}

[DefaultTag("IPlatformRender")]
internal sealed class LinuxRenderEngineComponent : EngineComponent, IPlatformRender
{
    public event Action? OnResuming
    {
        add { }
        remove { }
    }
    public event Action? OnSuspending
    {
        add { }
        remove { }
    }

    public void SuspendRenderContext() { }

    public void ResumeRenderContext() { }

    public Rational GetMonitorDefaultRefreshRate(nint hMonitor) => Rational.Zero;

    public bool IsDeveloperModeEnabled() => false;
}

[DefaultTag("IPlatformSystem")]
internal sealed class LinuxSystemEngineComponent : EngineComponent, IPlatformSystem
{
    private static readonly IPlatformDiagnostics PlatformDiagnostics = new LinuxDiagnostics();

    public IPlatformSystem.SimulationQualityEnum SimulationQuality =>
        IPlatformSystem.SimulationQualityEnum.Normal;
    public bool IsSingleInstance => true;
    public bool IsDeprecatedOS => false;
    public string ThreeLetterISORegionName =>
        GetRegion(static region => region.ThreeLetterISORegionName);
    public string TwoLetterISORegionName =>
        GetRegion(static region => region.TwoLetterISORegionName);
    public string RegionLatitude => string.Empty;
    public string RegionLongitude => string.Empty;
    public IPlatformDiagnostics Diagnostics => PlatformDiagnostics;
    public float CPULoad => 0;
    public event Action<string>? OnSystemProtocolActivated
    {
        add { }
        remove { }
    }

    public void LogEnvironmentInformation()
    {
        Log?.WriteLine($"OS: {RuntimeInformation.OSDescription}");
        Log?.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        Log?.WriteLine($"Processors: {Environment.ProcessorCount}");
    }

    public string GetInfoCPU() => GetInfoCPU(out _, out _);

    public string GetInfoCPU(out uint physicalCores, out uint logicalCores)
    {
        physicalCores = logicalCores = (uint)Math.Max(1, Environment.ProcessorCount);
        return RuntimeInformation.ProcessArchitecture.ToString();
    }

    public uint? GetMaxClockSpeedCpuMHz() => null;

    public bool? IsHardwareAcceleratedGpuSchedulingEnabled() => null;

    public Keen.VRage.Library.Filesystem.DriveType? GetProcessDriveType() =>
        Keen.VRage.Library.Filesystem.DriveType.Unspecified;

    public void OnSessionStarted(IPlatformSystem.SessionType sessionType) { }

    public void OnSessionUnloaded() { }

    public void ResetColdStartRegister() { }

    public bool OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public DateTime GetNetworkTimeUTC() => DateTime.UtcNow;

    public TimeSpan GetJitCompilationTime() => JitInfo.GetCompilationTime(currentThread: true);

    public void LogToExternalDebugger(string message) => Debug.WriteLine(message);

    /// <summary>
    /// The Windows implementation reads a per-thread hardware cache-miss counter. Linux exposes
    /// the same data only through <c>perf_event_open</c>, which needs elevated permissions, so
    /// the counter is reported as unavailable.
    /// </summary>
    public ulong GetThreadCacheMisses() => 0uL;

    private static string GetRegion(Func<RegionInfo, string> selector)
    {
        try
        {
            return selector(RegionInfo.CurrentRegion);
        }
        catch (CultureNotFoundException)
        {
            return string.Empty;
        }
    }

    private sealed class LinuxDiagnostics : IPlatformDiagnostics
    {
        public void ThrowExceptionInMessageLoop() { }

        public void CreateAccessViolation() { }
    }
}

[DefaultTag("IPlatformWindows")]
internal sealed class LinuxWindowsEngineComponent : EngineComponent, IPlatformWindows
{
    private bool _hidden;
    private bool _delayShowing;
    private bool _applicationReady;

    public nint WindowHandle { get; set; }
    public IPlatformWindow Window { get; private set; } = null!;
    public TimeSpan DoubleClickTime => TimeSpan.FromMilliseconds(500);
    public Vector2I DoubleClickSize => new(4, 4);

    [Init]
    private void Init(PlatformObjectBuilder objectBuilder)
    {
        base.Init();
        _hidden = objectBuilder.HiddenWindow;
    }

    public Keen.VRage.Library.Threading.Task<string> GetClipboardTextAsync() =>
        System.Threading.Tasks.Task.Run(SdlThread.GetClipboardText);

    public VrTask SetClipboardTextAsync(string text) =>
        System.Threading.Tasks.Task.Run(() => SdlThread.SetClipboardText(text ?? string.Empty));

    public MessageBoxResult MessageBox(string text, string caption, MessageBoxOptions options) =>
        Singleton<VRageCore>.Instance.PlatformCore.CrashReporter.MessageBox(text, caption, options);

    public MessageBoxResult MessageBoxLocalized(
        LocKey text,
        LocKey caption,
        MessageBoxOptions options,
        Dictionary<string, object>? formatArguments = null
    ) =>
        Singleton<VRageCore>.Instance.PlatformCore.CrashReporter.MessageBoxLocalized(
            text,
            caption,
            options,
            formatArguments
        );

    public void SetRenderWindow(IPlatformWindow window)
    {
        Window = window;
        WindowHandle = window.WindowHandle;
        if (window is SdlPlatformWindow sdlWindow)
            sdlWindow.SetShowAllowed(!_hidden && (!_delayShowing || _applicationReady));
        if (_hidden || (_delayShowing && !_applicationReady))
            window.Hide();
    }

    public void DelayWindowShowingUntilApplicationIsReady()
    {
        _delayShowing = true;
        if (Window != null && !_applicationReady)
            Window.Hide();
    }

    public void OnApplicationReady()
    {
        _applicationReady = true;
        SdlPlatformWindow? sdlWindow = Window as SdlPlatformWindow;
        LinuxSplashScreen.Close();
        if (_hidden || Window == null)
            return;

        VRageCore core = Singleton<VRageCore>.Instance;
        core.NotifyApplicationReady();
        sdlWindow?.SetShowAllowed(true);
        Window.ShowAndFocus();
        core.NotifyApplicationShown();
    }
}
