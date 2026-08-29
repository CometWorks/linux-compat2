using Keen.Game2;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Core;
using Keen.VRage.Library.Utils;
using Keen.VRage.UI.EngineComponents;
using Keen.VRage.UI.Shared.ViewModels;

namespace ClientPlugin.Tools;

internal static class GameAccess
{
    public static SharedUIComponent GetSharedUI()
    {
        var engine = Singleton<VRageCore>.Instance?.Engine;
        if (engine == null)
            return null;

#pragma warning disable CA1416 // LinuxCompat replaces the component's Windows dependencies.
        return engine.Get<GameAppComponent>()?.GetSharedUI();
#pragma warning restore CA1416
    }

    public static IViewModelFactory GetViewModelFactory()
    {
        var engine = Singleton<VRageCore>.Instance?.Engine;
        if (engine == null)
            return null;

        return engine.Get<ViewModelFactoryComponent>();
    }
}
