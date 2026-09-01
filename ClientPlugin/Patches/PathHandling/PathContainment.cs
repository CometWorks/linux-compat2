using System;
using System.Collections.Generic;
using System.IO;
using LinuxCompat.Platform;

namespace LinuxCompat.Patches.PathHandling;

/// <summary>
/// Decides whether a project search path the game is about to cache stays inside the
/// game's own folders, and canonicalises it so the decision is made on the real path
/// rather than on the string a mod shipped.
///
/// Published workshop mods carry <c>ProjectDirectories</c> from their author's machine:
/// Windows absolute paths (<c>F:\Documents\SpaceEngineers2\Mods\Speed</c>), relative
/// entries, even <c>..\..</c> traversals. <see cref="Keen.VRage.Core.Project.LocalProjectLocator"/>
/// resolves each with <c>Path.GetFullPath</c> — which leaves an absolute path untouched
/// and silently collapses <c>..\..</c> — then adds the result as a search path that the
/// locator enumerates, reads <c>.vrgproj</c> files from, and (via
/// <c>Directory.CreateDirectory(&lt;dir&gt;/Content)</c>) writes into. On Linux the raw
/// Windows paths resolve to nothing and the escape is invisible, so nothing looks wrong;
/// the reach is the problem, not the breakage.
///
/// Containment is tested on the RESOLVED, symlink-canonicalised form, because
/// <c>..\..</c> is already gone by the time the string is a real path. Roots are compared
/// with a trailing separator so that <c>/games/SpaceEngineers2Evil</c> does not pass as a
/// prefix of <c>/games/SpaceEngineers2</c>.
/// </summary>
internal static class PathContainment
{
    private static readonly char Sep = Path.DirectorySeparatorChar;

    private static readonly Lazy<IReadOnlyList<string>> Roots = new(BuildRoots);

    /// <summary>
    /// The game's own directory trees, canonicalised. A cached search path is legitimate
    /// only if it resolves to somewhere under one of these.
    /// </summary>
    private static IReadOnlyList<string> BuildRoots()
    {
        var roots = new List<string>();

        // Pulsar sets the working directory to Game2 after preloaders finish and before the
        // game starts. Unlike the preloaded assembly location, this remains in the installation.
        string? installRoot = Path.GetDirectoryName(Environment.CurrentDirectory);
        if (installRoot is { Length: > 0 })
        {
            roots.Add(installRoot);

            // The shipped Vanilla project lists "..\..\VRage\GameData", which on the Windows
            // layout is a sibling of the install folder. The Linux layout has no such sibling,
            // so the probe misses harmlessly — but it is the game's OWN construction, not a
            // mod's, so it is allow-listed here rather than reported as an escape.
            string? common = Path.GetDirectoryName(installRoot);
            if (common is { Length: > 0 })
            {
                roots.Add(Path.Combine(common, "VRage", "GameData"));

                // The Mod SDK is a first-party SE2 content root some projects reference.
                roots.Add(Path.Combine(common, "Space Engineers 2 - Mod SDK"));

                // The Steam workshop content for app 1133870: .../steamapps/workshop/content/1133870.
                string? steamapps = Path.GetDirectoryName(common);
                if (steamapps is { Length: > 0 })
                    roots.Add(Path.Combine(steamapps, "workshop", "content", "1133870"));
            }
        }

        // The SE2 data folder (~/.config/SpaceEngineers2, honouring -appData:): AppData holds
        // LocalMods and SaveGames, Temp holds the local mod index the locator mounts from.
        string dataRoot = LinuxDataFolder.Resolve();
        if (dataRoot is { Length: > 0 })
            roots.Add(dataRoot);

        var canonical = new List<string>(roots.Count);
        foreach (string root in roots)
        {
            string c = Canonicalize(root);
            if (c.Length > 0 && !canonical.Contains(c))
                canonical.Add(c);
        }
        return canonical;
    }

    /// <summary>
    /// Resolve a path to its canonical absolute form, following symlinks on whatever leading
    /// portion of it exists. A trailing portion that does not exist yet (a project directory
    /// the game has not created) is kept verbatim, so containment is still decided against the
    /// real parent. Never throws: on any failure it returns the plain <c>GetFullPath</c> form.
    /// </summary>
    public static string Canonicalize(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }

        string current = Sep.ToString();
        foreach (string part in full.Split(Sep, StringSplitOptions.RemoveEmptyEntries))
        {
            string next = Path.Combine(current, part);
            try
            {
                FileSystemInfo? info =
                    Directory.Exists(next) ? new DirectoryInfo(next)
                    : File.Exists(next) ? new FileInfo(next)
                    : null;
                if (info?.LinkTarget != null)
                {
                    FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
                    if (target is { FullName.Length: > 0 })
                        next = Path.GetFullPath(target.FullName);
                }
            }
            catch
            {
                // A component we cannot stat is kept as-is; the parent's canonical form still
                // anchors the containment decision.
            }

            current = next;
        }

        return current;
    }

    /// <summary>
    /// True if <paramref name="canonicalPath"/> (already canonicalised) is one of the game's
    /// roots or lives under one. The trailing-separator comparison prevents a sibling whose
    /// name merely starts with a root's name from passing.
    /// </summary>
    public static bool IsWithinGameRoots(string canonicalPath)
    {
        foreach (string root in Roots.Value)
        {
            if (canonicalPath == root)
                return true;

            string rootWithSep = root.EndsWith(Sep) ? root : root + Sep;
            if (canonicalPath.StartsWith(rootWithSep, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
