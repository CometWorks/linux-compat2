using System.Reflection;
using HarmonyLib;
using Keen.VRage.Library.Filesystem;

namespace LinuxCompat.Patches;

internal static class NativeFileSystemPathCasePatch
{
    public static void Install()
    {
        Type helpers = typeof(FileSystemHelpers);
        Type nativeFileSystem = helpers.Assembly.GetType("Keen.VRage.Library.Filesystem.NativeFileSystem", throwOnError: true)!;
        MethodInfo caseTarget = AccessTools.DeclaredMethod(helpers, "ToLowerInvariantCached", [typeof(string)])
            ?? throw new MissingMethodException(helpers.FullName, "ToLowerInvariantCached");
        MethodInfo normalizeTarget = AccessTools.DeclaredMethod(helpers, nameof(FileSystemHelpers.NormalizePath), [typeof(string)])
            ?? throw new MissingMethodException(helpers.FullName, nameof(FileSystemHelpers.NormalizePath));
        MethodInfo safeHandleTarget = AccessTools.DeclaredMethod(nativeFileSystem, "TryOpenReadSafeHandle",
            [typeof(string), typeof(AccessHandle).MakeByRefType(), typeof(FileShare), typeof(AdvancedFileOptions)])
            ?? throw new MissingMethodException(nativeFileSystem.FullName, "TryOpenReadSafeHandle");
        Harmony harmony = new("LinuxCompat.NativeFileSystemPaths");
        harmony.Patch(caseTarget, prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(NativeFileSystemPathCasePatch), nameof(CasePrefix))!));
        harmony.Patch(normalizeTarget, prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(NativeFileSystemPathCasePatch), nameof(NormalizePrefix))!));
        harmony.Patch(safeHandleTarget, prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(NativeFileSystemPathCasePatch), nameof(ResolveFilePrefix))!));
    }

    private static bool CasePrefix(string __0, ref string __result)
    {
        __result = ResolvePathCase(__0);
        return false;
    }

    private static void NormalizePrefix(ref string __0)
    {
        __0 = __0.Replace('\\', Path.DirectorySeparatorChar);
    }

    private static void ResolveFilePrefix(ref string __0)
    {
        __0 = ResolvePathCase(__0);
    }

    private static string ResolvePathCase(string path)
    {
        string normalized = path.Replace('\\', Path.DirectorySeparatorChar);
        if (normalized.Length == 0 || File.Exists(normalized) || Directory.Exists(normalized))
            return normalized;

        string basePath = Environment.CurrentDirectory;
        bool rooted = Path.IsPathRooted(normalized);
        string absolute = Path.GetFullPath(normalized, basePath);
        string root = Path.GetPathRoot(absolute)!;
        string current = root;
        string[] segments = absolute[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            string? exact = null;
            string? insensitive = null;
            bool ambiguous = false;
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                {
                    string name = Path.GetFileName(entry);
                    if (string.Equals(name, segments[i], StringComparison.Ordinal))
                    {
                        exact = entry;
                        break;
                    }
                    if (!string.Equals(name, segments[i], StringComparison.OrdinalIgnoreCase))
                        continue;
                    ambiguous = insensitive != null;
                    insensitive ??= entry;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            string? match = exact ?? (ambiguous ? null : insensitive);
            if (match == null)
            {
                for (; i < segments.Length; i++)
                    current = Path.Combine(current, segments[i]);
                break;
            }
            current = match;
        }
        return rooted ? current : Path.GetRelativePath(basePath, current);
    }
}
