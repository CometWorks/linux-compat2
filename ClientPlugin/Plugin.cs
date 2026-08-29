using System;
using ClientPlugin.Settings;
using ClientPlugin.Tools;
using Keen.VRage.Core.Plugins;
using Keen.VRage.Library.Diagnostics;

namespace ClientPlugin;

public class Plugin : IPlugin
{
    public const string Name = "LinuxCompat";
    public static Plugin Instance;

#pragma warning disable CS0649 // This field is assigned by Pulsar
    private static Func<string, string, string> GetConfigPath;
#pragma warning restore CS0649
    public string DataDir { get; private set; } = GetConfigPath(Name, null);

    public Plugin()
    {
        Instance = this;

        _ = Config.Current;

        Log.Default.WriteLine($"[{Name}] Loaded plugin.");
    }

    // Invoked by Pulsar via reflection when the user clicks the plugin's config button.
    public void OpenConfigDialog()
    {
        try
        {
            var sharedUi = GameAccess.GetSharedUI();
            if (sharedUi == null)
            {
                Log.Default.WriteLine(
                    LogSeverity.Warning,
                    $"[{Name}] SharedUIComponent not available"
                );
                return;
            }

            var generator = new SettingsGenerator();
            var viewModel = new SettingsScreenViewModel(
                generator.Title,
                panel => generator.PopulateContent(panel),
                () => ConfigStorage.Save(Config.Current)
            );

            sharedUi.CreateScreen<SettingsScreen>(viewModel, showCursor: true);
        }
        catch (Exception e)
        {
            Log.Default.WriteLine(LogSeverity.Error, $"[{Name}] OpenConfigDialog failed: {e}");
        }
    }
}
