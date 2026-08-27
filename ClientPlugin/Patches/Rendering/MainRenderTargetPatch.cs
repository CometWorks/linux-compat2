using HarmonyLib;
using Keen.Game2.Simulation.GameSystems.Saves;
using Keen.VRage.Library.Definitions;
using Keen.VRage.Library.Filesystem;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Library.Utils;
using Keen.VRage.Render.Contracts;

namespace LinuxCompat.Patches.Rendering;

/// <summary>
/// Bounds save-game thumbnail captures to <see cref="SavesConfiguration.MaxThumbnailSize" />.
/// The shipped producer in <c>SaveGameTrackerSessionComponent.TryCaptureThumbnailAsync</c> always
/// fixes its downsample request to <c>MaxThumbnailSize.Y</c>, so ultrawide sources can exceed
/// <c>MaxThumbnailSize.X</c>, and a null request lets a hot-resized window grow the thumbnail
/// without bound. The producer's async state machine carries an exception filter
/// (<c>when (ex.IsDiskFull())</c>) that Harmony 2.4.2 cannot round-trip, so the clamp sits on the
/// filter-free <see cref="MainRenderTarget.TakeScreenshotAsync" /> and recognizes the thumbnail
/// capture by its unique "thumb.jpg" target file name.
/// </summary>
[HarmonyPatch(typeof(MainRenderTarget), nameof(MainRenderTarget.TakeScreenshotAsync))]
[HarmonyPatchCategory("Finish")]
internal static class MainRenderTargetPatch
{
    private const string ThumbnailFileName = "thumb.jpg";

    static void Prefix(FileHandleWritable saveFile, ref Vector2I? downsampleResolution)
    {
        if (saveFile.Path == null || Path.GetFileName(saveFile.Path) != ThumbnailFileName)
            return;
        if (
            !Singleton<DefinitionManager>.Instance.TryGetConfiguration<SavesConfiguration>(
                out var configuration
            ) || configuration is null
        )
            return;
        downsampleResolution = ScreenshotsManagerPatch.FitWithin(
            downsampleResolution ?? configuration.MaxThumbnailSize,
            configuration.MaxThumbnailSize
        );
    }
}
