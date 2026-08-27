using Keen.VRage.Core;
using LinuxCompat.Platform;

namespace LinuxCompat.Patches.PlatformGuards;

public static class ProgramPatch
{
    /// <summary>
    /// Replaces the eager <c>new VRageWindows()</c> construction in <c>Program.Main</c>.
    /// Patch installation happens in the Pulsar preloader hook; by the time this runs the
    /// resolvers and Harmony patches are already in place.
    /// </summary>
    public static IPlatformFactory SelectPlatformFactory() => new LinuxPlatformFactory();

    /// <summary>
    /// Replaces the <c>Program.CheckInstallPathLength</c> call in Main. The original method
    /// reports over-long Windows install paths with a WinForms message box; its body cannot
    /// even be read by Harmony on Linux because System.Windows.Forms is not resolvable, and
    /// Linux has no such path limit, so the call is redirected rather than the method patched.
    /// </summary>
    public static bool CheckInstallPath(string[] args) => true;
}
