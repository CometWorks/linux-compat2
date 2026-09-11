using Keen.VRage.Core.Plugins;
using Keen.VRage.Library.Diagnostics;

namespace ClientPlugin;

// LinuxCompat does all of its work from the Preloader, which runs long before Pulsar
// instantiates plugins. This type exists so Pulsar has an IPlugin to load and so the
// plugin shows up as active in its UI; it has no settings and no per-frame work.
public class Plugin : IPlugin
{
    public const string Name = "LinuxCompat";

    public Plugin() => Log.Default.WriteLine($"[{Name}] Loaded plugin.");
}
