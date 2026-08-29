using HarmonyLib;
using Keen.VRage.Render.Data;
using Keen.VRage.Render12.Core.Systems;
using LinuxCompat.Platform;

namespace LinuxCompat.Patches.Rendering;

/// <summary>
/// Steers FSR upscaling onto the FSR 3.1 provider, and falls back to FXAA when
/// the native upscaler is not there.
///
/// Raising the graphics Quality setting selects <see cref="AAMode.FSR"/>, and the
/// renderer then creates an upscaler context through AMD's
/// <c>amd_fidelityfx_loader_dx12.dll</c>. The bundled dependencies supply that as
/// <c>libamd_fidelityfx_loader_dx12.so</c>, built from the FidelityFX SDK. Only the
/// FSR 3.1.5 provider is in it: FSR 4.x has no source, so it cannot be part of a
/// Linux build. Forcing <c>ForceUseFSR_3_1</c> makes the renderer ask for the
/// provider that exists — the game's own "FSR upscaler provider selected: …" log
/// line then names it — instead of picking a default that may not be there.
///
/// The library is still probed before FSR is allowed through. <c>FSR4_1.TryCreateContext</c>
/// does not guard against a failing context despite its name, so a missing or
/// unloadable library would throw <c>DllNotFoundException</c> on the render thread
/// and kill the game. Reporting FXAA instead makes <c>UpsamplingJob</c> take its
/// bilinear path, which both <see cref="AAMode.None"/> and <see cref="AAMode.FXAA"/>
/// already use, so upscaling keeps working at a lower quality rather than crashing.
///
/// The getter returns a copy of the settings struct, so the setting the player chose
/// is left untouched; only what the renderer acts on changes.
/// </summary>
[HarmonyPatch(typeof(SettingsManager), nameof(SettingsManager.DRS), MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
internal static class UpsamplingPatch
{
    private const string Library = "amd_fidelityfx_loader_dx12.dll";

    // The getter runs per frame; probe the library once and reuse the answer.
    private static readonly Lazy<bool> Available = new(() =>
        LinuxNativeLibraryResolver.CanLoad(Library)
    );

    private static int _reported;

    static void Postfix(ref DRSSettings __result)
    {
        __result.ForceUseFSR_3_1 = true;

        if (__result.AAMode != AAMode.FSR || Available.Value)
            return;

        __result.AAMode = AAMode.FXAA;

        // Report the substitution only the first time.
        if (Interlocked.Exchange(ref _reported, 1) == 0)
            Console.WriteLine(
                "[LinuxCompat] FSR upscaling needs libamd_fidelityfx_loader_dx12.so "
                    + "from the bundled dependencies; falling back to FXAA with bilinear upscaling."
            );
    }
}
