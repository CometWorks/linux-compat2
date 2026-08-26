using System.Runtime.InteropServices;
using Keen.VRage.Library.Diagnostics;

namespace LinuxCompat.Patches;

/// <summary>
/// Replaces the Windows edition lookup that the renderer logs while initializing.
///
/// <c>Render12EngineComponent.Init</c>'s <c>PrintOSDetails</c> local function names the
/// Windows edition through kernel32's <c>GetProductInfo</c>. On Linux that P/Invoke throws
/// <see cref="DllNotFoundException"/>, which the game catches and logs with its full stack
/// trace, so the first exception in every render log is one that means nothing. The
/// distribution and the kernel release are the informative Linux equivalents, so log those
/// instead of the edition that cannot be determined.
/// </summary>
internal static class OsDetailsPatch
{
    public static bool PrintOsDetails(Log log)
    {
        // OSDescription is the os-release pretty name on Linux; the OS Version line the
        // renderer logs just above this one carries only the kernel version numbers.
        log.WriteLine("OS Product: " + RuntimeInformation.OSDescription);
        log.WriteLine("OS Kernel: " + ReadKernelRelease());
        return false;
    }

    /// <summary>
    /// Reads the full kernel release (what <c>uname -r</c> prints, including the
    /// distribution's flavour suffix) without spawning a process. Failure falls back to a
    /// placeholder rather than throwing out of a logging call.
    /// </summary>
    private static string ReadKernelRelease()
    {
        try
        {
            return File.ReadAllText("/proc/sys/kernel/osrelease").Trim();
        }
        catch (Exception e)
        {
            return $"<No info: {e.Message}>";
        }
    }
}
