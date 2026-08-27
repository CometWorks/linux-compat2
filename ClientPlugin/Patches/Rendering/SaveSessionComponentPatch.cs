using System.Runtime.CompilerServices;
using HarmonyLib;
using Keen.Game2.Simulation.RuntimeSystems.Saves;

namespace LinuxCompat.Patches.Rendering;

/// <summary>
/// Holds the startup autosave back until the voxel terrain has streamed in, so the save
/// thumbnail of a freshly created world shows the world instead of the sky below it.
/// <para>
/// <c>SaveSessionComponent.Init</c> arms a 10 ms repeating timer whose <c>StartAutosave</c>
/// tick saves as soon as <c>IWorldLoadedListener.OnWorldLoaded</c> has run, which is the
/// world's very first rendered frames. The thumbnail is a plain copy of the frame buffer
/// (<c>SaveGameTrackerSessionComponent.TryCaptureThumbnailAsync</c> to
/// <c>MainRenderTarget.TakeScreenshotAsync</c> to the pre-UI copy of the final LDR buffer),
/// and on Linux the voxel meshes need about five more seconds, so the capture catches
/// serialized grids floating in front of the sky with the ground missing. The prefix
/// declines every tick until <see cref="StartupAutosaveDelay" /> has passed; the repeating
/// timer keeps polling, so the startup autosave happens later instead of not at all.
/// </para>
/// </summary>
[HarmonyPatch(typeof(SaveSessionComponent), nameof(SaveSessionComponent.StartAutosave))]
[HarmonyPatchCategory("Finish")]
internal static class SaveSessionComponentPatch
{
    /// <summary>Settle time measured from the first tick that observes the loaded world.
    /// Terrain appears after roughly five seconds, so this leaves the same margin again.</summary>
    public static readonly TimeSpan StartupAutosaveDelay = TimeSpan.FromSeconds(10);

    private static readonly ConditionalWeakTable<SaveSessionComponent, StrongBox<long>> Deadlines =
        new();

    /// <summary>Lets the tick through once the world has been loaded for at least
    /// <see cref="StartupAutosaveDelay" />. Ticks before the world is loaded pass through
    /// unchanged, because the shipped tick does nothing until then.</summary>
    static bool Prefix(SaveSessionComponent __instance)
    {
        if (!__instance._worldLoaded)
            return true;

        StrongBox<long> deadline = Deadlines.GetValue(
            __instance,
            _ => new StrongBox<long>(
                Environment.TickCount64 + (long)StartupAutosaveDelay.TotalMilliseconds
            )
        );
        return Environment.TickCount64 >= deadline.Value;
    }
}
