using HarmonyLib;
using Keen.VRage.Core.Project;

namespace LinuxCompat.Patches.PathHandling;

/// <summary>
/// The shipped project files list their <c>ProjectDirectories</c> with Windows backslash
/// separators (for example <c>..\..\VRage\GameData</c>). Linux treats backslashes as literal
/// name characters, so the project locator's cached search paths never exist and dependency
/// projects such as the Engine project cannot be found. Normalizing every added search path
/// keeps the shipped data files untouched.
/// </summary>
[HarmonyPatch(typeof(LocalProjectLocator), nameof(LocalProjectLocator.AddSearchPath))]
[HarmonyPatchCategory("Finish")]
internal static class ProjectLocatorPatch
{
    static void Prefix(ref string path)
    {
        if (!path.Contains('\\'))
            return;

        string normalized = path.Replace('\\', Path.DirectorySeparatorChar);
        path = Path.IsPathRooted(normalized) ? Path.GetFullPath(normalized) : normalized;
    }
}
