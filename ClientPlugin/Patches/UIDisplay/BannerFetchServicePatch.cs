using HarmonyLib;
using Keen.Game2.Client.UI.Menu.News;
using Keen.VRage.Library.Filesystem;

namespace LinuxCompat.Patches.UIDisplay;

[HarmonyPatch(
    typeof(BannerFetchService),
    nameof(BannerFetchService.DownloadImageAsync),
    typeof(string),
    typeof(FileHandleWritable),
    typeof(object)
)]
[HarmonyPatchCategory("Finish")]
internal static class BannerFetchServiceDownloadImageAsyncPatch
{
    private const string ContentOrigin = "https://content-v3.keenswh.com";

    static void Prefix(ref string imageUrl)
    {
        if (
            imageUrl.StartsWith("/", StringComparison.Ordinal)
            && !imageUrl.StartsWith("//", StringComparison.Ordinal)
        )
            imageUrl = ContentOrigin + imageUrl;
    }
}
