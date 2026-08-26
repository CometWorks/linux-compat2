using System.Reflection;

namespace LinuxCompat.Platform;

/// <summary>
/// Last-chance managed assembly resolver serving the plugin's Bin directory. Its main job is
/// resolving the bundled Windows Desktop implementation assemblies (System.Windows.Forms and
/// friends): nothing on the Linux code path executes them, but game assemblies and Pulsar
/// patches reference WinForms types, and reading or JIT compiling such methods requires the
/// tokens to resolve. Registered after Pulsar's own resolvers, so the game directory wins
/// for anything it ships.
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
        string? directory = Path.GetDirectoryName(typeof(ManagedAssemblyResolver).Assembly.Location);
        if (directory is not { Length: > 0 })
            return null;

        string path = Path.Combine(directory, new AssemblyName(args.Name).Name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
}
