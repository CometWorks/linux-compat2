using HarmonyLib;
using Keen.VRage.Library.Filesystem;

namespace LinuxCompat.Patches.PathHandling;

[HarmonyPatch(typeof(FileSystemHelpers), nameof(FileSystemHelpers.ToLowerInvariantCached))]
[HarmonyPatchCategory("Finish")]
internal static class FileSystemHelpersToLowerInvariantCachedPatch
{
    static bool Prefix(string path, ref string __result)
    {
        __result = NativeFileSystemPathCase.Resolve(path);
        return false;
    }
}

[HarmonyPatch(typeof(FileSystemHelpers), nameof(FileSystemHelpers.NormalizePath))]
[HarmonyPatchCategory("Finish")]
internal static class FileSystemHelpersNormalizePathPatch
{
    static void Prefix(ref string path) => path = path.Replace('\\', Path.DirectorySeparatorChar);
}

[HarmonyPatch(typeof(NativeFileSystem), nameof(NativeFileSystem.TryOpenReadSafeHandle))]
[HarmonyPatchCategory("Finish")]
internal static class NativeFileSystemTryOpenReadSafeHandlePatch
{
    static void Prefix(ref string file) => file = NativeFileSystemPathCase.Resolve(file);
}

internal static class NativeFileSystemPathCase
{
    public static string Resolve(string path)
    {
        string normalized = path.Replace('\\', Path.DirectorySeparatorChar);
        if (normalized.Length == 0 || File.Exists(normalized) || Directory.Exists(normalized))
            return normalized;

        string basePath = Environment.CurrentDirectory;
        bool rooted = Path.IsPathRooted(normalized);
        string absolute = Path.GetFullPath(normalized, basePath);
        string root = Path.GetPathRoot(absolute)!;
        string current = root;
        string[] segments = absolute[root.Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
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
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

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
