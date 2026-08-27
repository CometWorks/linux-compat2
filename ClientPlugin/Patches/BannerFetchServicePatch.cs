using System.Reflection;
using HarmonyLib;

namespace LinuxCompat.Patches;

public static class BannerFetchServicePatch
{
    private const string ContentOrigin = "https://content-v3.keenswh.com";
    private static int _installed;

    public static void Install()
    {
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            TryPatch(assembly);
    }

    public static string ResolveImageUrl(string url) =>
        url.StartsWith("/", StringComparison.Ordinal) && !url.StartsWith("//", StringComparison.Ordinal)
            ? ContentOrigin + url
            : url;

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args) => TryPatch(args.LoadedAssembly);

    private static void TryPatch(Assembly assembly)
    {
        if (assembly.GetName().Name != "Game2.Client" || Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        Type service = assembly.GetType("Keen.Game2.Client.UI.Menu.News.BannerFetchService", throwOnError: true)!;
        MethodInfo target = service.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(method =>
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.Name == "DownloadImageAsync"
                && parameters.Length == 3
                && parameters[0].ParameterType == typeof(string)
                && parameters[1].ParameterType.FullName == "Keen.VRage.Library.Filesystem.FileHandleWritable"
                && parameters[2].ParameterType == typeof(object);
        });
        new Harmony("LinuxCompat.BannerFetchService").Patch(
            target,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(BannerFetchServicePatch), nameof(Prefix))!));
    }

    private static void Prefix(ref string __0) => __0 = ResolveImageUrl(__0);
}
