using System.Reflection;
using System.Runtime.Loader;

namespace LinuxCompat.Platform;

internal static class ManagedAssemblyResolver
{
    public static void Install()
    {
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        string file = name.Name + ".dll";
        string path = Path.Combine(AppContext.BaseDirectory, file);
        if (!File.Exists(path))
            path = Path.Combine(Environment.CurrentDirectory, file);
        return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    }
}
