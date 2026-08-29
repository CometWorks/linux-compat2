using Keen.VRage.Core;
using Keen.VRage.Core.EngineComponents;
using Keen.VRage.Core.Input;
using Keen.VRage.Render.CoreConfigurations;
using Keen.VRage.Render.EngineComponents;
using LinuxCompat.Platform;

namespace LinuxCompat.Patches.PlatformGuards;

public static class GameAppPatch
{
    public static void AddPlatform(
        EngineBuilder builder,
        PlatformObjectBuilder platformObjectBuilder
    )
    {
        builder.Add<LinuxSystemEngineComponent>();
        builder.Add<LinuxRenderEngineComponent>();
        builder.Add<LinuxMemoryEngineComponent>();
        builder.Add<LinuxWindowsEngineComponent>(platformObjectBuilder);
        builder.Add<SdlInputComponent>();
    }

    public static void ConfigureRender(
        RenderConfigurationObjectBuilder configuration,
        RenderObjectBuilder render
    )
    {
        configuration.UseDirectStorage = false;
        if (LinuxNativeLibraryResolver.IsEnabled("SE2_CPU_RENDERING"))
            configuration.ForceAllAdaptersSupported = true;
        render.CreateWindowFunc = _ => new SdlPlatformWindow("Space Engineers 2", 1600, 900);
        if (
            int.TryParse(Environment.GetEnvironmentVariable("SE2_MAX_FPS"), out int maxFps)
            && maxFps is >= 1 and <= 240
        )
            render.MaxFrameRate = maxFps;
    }
}
