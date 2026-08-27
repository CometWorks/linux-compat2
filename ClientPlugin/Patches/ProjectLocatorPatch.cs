using System.Reflection;
using HarmonyLib;

namespace LinuxCompat.Patches;

/// <summary>
/// The shipped project files list their <c>ProjectDirectories</c> with Windows backslash
/// separators (for example <c>..\..\VRage\GameData</c>). Linux treats backslashes as literal
/// name characters, so the project locator's cached search paths never exist and dependency
/// projects such as the Engine project cannot be found. Normalizing every added search path
/// keeps the shipped data files untouched.
/// </summary>
internal static class ProjectLocatorPatch
{
    public static void Install()
    {
        Type locator = AccessTools.TypeByName("Keen.VRage.Core.Project.LocalProjectLocator")
            ?? throw new TypeLoadException("LocalProjectLocator was not found.");
        MethodInfo target = AccessTools.DeclaredMethod(locator, "AddSearchPath", [typeof(string)])
            ?? throw new MissingMethodException(locator.FullName, "AddSearchPath");
        new Harmony("LinuxCompat.ProjectLocator").Patch(target,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(ProjectLocatorPatch), nameof(Prefix))!));
    }

    private static void Prefix(ref string __0)
    {
        if (!__0.Contains('\\'))
            return;

        string normalized = __0.Replace('\\', Path.DirectorySeparatorChar);
        __0 = Path.IsPathRooted(normalized) ? Path.GetFullPath(normalized) : normalized;
    }
}
