using System.Reflection;

namespace LinuxCompat.Platform;

/// <summary>
/// Last-chance managed assembly resolver serving the plugin's Bin directory, so managed
/// assemblies arriving with the dependency archives (Steamworks.NET) are found even when the
/// requesting assembly was loaded from elsewhere. Registered after Pulsar's own resolvers,
/// so the game directory wins for anything it ships.
/// </summary>
internal static class ManagedAssemblyResolver
{
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
    }

    private static Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        string? directory = Path.GetDirectoryName(
            typeof(ManagedAssemblyResolver).Assembly.Location
        );
        if (directory is not { Length: > 0 })
            return null;

        string path = Path.Combine(directory, new AssemblyName(args.Name).Name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
}
