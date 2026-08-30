using HarmonyLib;
using Keen.VRage.Core.Project;
using Keen.VRage.Library.Diagnostics;

namespace LinuxCompat.Patches.PathHandling;

/// <summary>
/// Guards every search path the project locator is about to cache.
///
/// Two jobs, in order. First, normalisation: the shipped project files list their
/// <c>ProjectDirectories</c> with Windows backslash separators (for example
/// <c>..\..\VRage\GameData</c>). Linux treats backslashes as literal name characters, so
/// without normalisation the locator's cached paths never exist and dependency projects
/// such as the Engine project cannot be found. Normalising here also lets
/// <c>Path.GetFullPath</c> collapse the <c>..\..</c> that a backslash path hides on Linux.
///
/// Second, containment: a mod ships arbitrary path strings and the game resolves them on
/// the player's machine — an absolute <c>F:\Documents\…</c>, or a <c>..\..\..</c> that
/// walks out of the mod folder. The resolved path is added as a search path that
/// <c>LoadProjectAsync</c> enumerates and reads <c>.vrgproj</c> files from, and that
/// <c>GetProject</c> writes a <c>Content</c> directory into. So the resolved, symlink-
/// canonicalised path is tested against the game's own roots (see
/// <see cref="PathContainment"/>); anything outside is refused and logged as an error
/// naming both the string as shipped and what it resolved to on this platform. The shipped
/// data files are never touched.
/// </summary>
[HarmonyPatch(typeof(LocalProjectLocator), nameof(LocalProjectLocator.AddSearchPath))]
[HarmonyPatchCategory("Finish")]
internal static class ProjectLocatorPatch
{
    static bool Prefix(ref string path)
    {
        string shipped = path;

        // Windows separators -> the platform separator, so GetFullPath can collapse "..".
        string normalized = path.Replace('\\', Path.DirectorySeparatorChar);

        // Resolve to an absolute, "..-free" path, then follow symlinks. Containment must be
        // judged on this form: by now "../.." is already gone, so the raw string cannot be
        // trusted to reveal where the path actually points.
        string canonical = PathContainment.Canonicalize(normalized);

        if (!PathContainment.IsWithinGameRoots(canonical))
        {
            // An error, not a warning: a mod search path pointing outside the game's own
            // folders is never legitimate, and the locator's own "directory does not exist"
            // line (logged only at Info, and only when the path happens not to exist) hides
            // it. Name both forms so a Windows "F:\Documents\…" entry shows what it became.
            Log.Default?.WriteLine(
                LogSeverity.Error,
                $"[LinuxCompat] Rejected a mod project search path outside the game's folders: "
                    + $"shipped as \"{shipped}\", resolved on this platform to \"{canonical}\". "
                    + "It was not added, so the game will not enumerate, read or write there."
            );
            return false; // skip AddSearchPath: the path is never cached
        }

        // Inside the game's folders: cache the fully resolved form.
        path = canonical;
        return true;
    }
}
