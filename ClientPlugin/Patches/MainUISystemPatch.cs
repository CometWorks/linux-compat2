using Keen.VRage.Library.Mathematics;
using Keen.VRage.Render.FrameData;

namespace LinuxCompat.Patches;

public static class MainUISystemPatch
{
    private static readonly object ResolutionLock = new();
    private static readonly Dictionary<RenderDrawCommandBuffer, Vector2> SubmittedBatchResolutions = new();
    private static Vector2 _layoutResolution;
    private static Vector2 _renderResolution;

    public static Vector2 ResolveViewportResolution(Vector2 viewportResolution)
    {
        lock (ResolutionLock)
        {
            if (_renderResolution.X <= 0f || _renderResolution.Y <= 0f)
                _layoutResolution = _renderResolution = viewportResolution;
            return _renderResolution;
        }
    }

    public static void LayoutUpdated(Vector2I resolution)
    {
        lock (ResolutionLock)
            _layoutResolution = new Vector2(resolution.X, resolution.Y);
    }

    public static void RecordSubmittedBatch(RenderDrawCommandBuffer drawBatch)
    {
        lock (ResolutionLock)
            SubmittedBatchResolutions[drawBatch] = _layoutResolution;
    }

    public static void BatchSubmitted(RenderDrawCommandBuffer drawBatch, int sortLayer)
    {
        lock (ResolutionLock)
        {
            if (SubmittedBatchResolutions.Remove(drawBatch, out Vector2 resolution)
                && sortLayer == 100 && drawBatch.RenderTarget == default)
                _renderResolution = resolution;
        }
    }
}
