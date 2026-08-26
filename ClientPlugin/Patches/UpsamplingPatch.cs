using System.Reflection;
using HarmonyLib;
using Keen.VRage.Render.Data;

namespace LinuxCompat.Patches;

/// <summary>
/// Falls back from FSR to FXAA upscaling on Linux.
///
/// Raising the graphics Quality setting selects <see cref="AAMode.FSR"/>, and the renderer
/// then creates an FSR 3.1 context through AMD's <c>amd_fidelityfx_dx12.dll</c>. That is a
/// Windows-only native library with no Linux counterpart in the bundled dependency set, so
/// the P/Invoke throws <c>DllNotFoundException</c> on the render thread and kills the game.
/// The engine's own <c>FSR3_1.TryCreateContext</c> does not guard against a failing context
/// despite its name, so the fallback has to happen before the mode is acted on.
///
/// Reporting FXAA instead makes <c>UpsamplingJob</c> take its bilinear path, which both
/// <see cref="AAMode.None"/> and <see cref="AAMode.FXAA"/> already use, so upscaling keeps
/// working at a lower quality rather than crashing. The setting the player chose is left
/// untouched; only what the renderer acts on changes.
/// </summary>
internal static class UpsamplingPatch
{
    public static void Install()
    {
        Type settings = Assembly.Load("VRage.Render12")
            .GetType("Keen.VRage.Render12.Core.Systems.SettingsManager", throwOnError: true)!;
        MethodInfo target = AccessTools.PropertyGetter(settings, "DRS")
            ?? throw new MissingMethodException(settings.FullName, "get_DRS");
        new Harmony("LinuxCompat.Upsampling").Patch(target,
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(UpsamplingPatch), nameof(Postfix))!));
    }

    private static void Postfix(ref DRSSettings __result)
    {
        if (OperatingSystem.IsLinux() && __result.AAMode == AAMode.FSR)
            __result.AAMode = AAMode.FXAA;
    }
}
