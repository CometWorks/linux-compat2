using Keen.VRage.Library.Mathematics;

namespace LinuxCompat.Patches.Rendering;

public static class ScreenshotsManagerPatch
{
    public static Vector2I FitWithin(Vector2I requested, Vector2I bounds)
    {
        if (requested.X <= bounds.X && requested.Y <= bounds.Y)
            return requested;

        float scale = MathF.Min((float)bounds.X / requested.X, (float)bounds.Y / requested.Y);
        return new Vector2I(
            Math.Clamp((int)MathF.Round(requested.X * scale), 1, bounds.X),
            Math.Clamp((int)MathF.Round(requested.Y * scale), 1, bounds.Y)
        );
    }
}
