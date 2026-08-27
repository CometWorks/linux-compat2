using HarmonyLib;
using Keen.Game2.Simulation.GameSystems.Saves;
using Keen.VRage.Library.Definitions;
using Keen.VRage.Library.Filesystem;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Library.Utils;
using Keen.VRage.Render.Contracts;

namespace LinuxCompat.Patches;

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
public static class MainRenderTargetPatch
{
    private const string ThumbnailFileName = "thumb.jpg";

    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        System.Reflection.MethodInfo target = AccessTools.DeclaredMethod(
                typeof(MainRenderTarget), nameof(MainRenderTarget.TakeScreenshotAsync))
            ?? throw new MissingMethodException(typeof(MainRenderTarget).FullName, "TakeScreenshotAsync");
        new Harmony("LinuxCompat.MainRenderTarget").Patch(
            target,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(
                typeof(MainRenderTargetPatch), nameof(Prefix))!));
    }

    public static void Prefix(FileHandleWritable saveFile, ref Vector2I? downsampleResolution)
    {
        if (saveFile.Path == null || Path.GetFileName(saveFile.Path) != ThumbnailFileName)
            return;
        if (!Singleton<DefinitionManager>.Instance.TryGetConfiguration<SavesConfiguration>(out var configuration)
            || configuration is null)
            return;
        downsampleResolution = ResolveThumbnailBounds(downsampleResolution, configuration.MaxThumbnailSize);
    }

    public static Vector2I? ResolveThumbnailBounds(Vector2I? requested, Vector2I maximum)
    {
        return ScreenshotsManagerPatch.FitWithin(requested ?? maximum, maximum);
    }
}
