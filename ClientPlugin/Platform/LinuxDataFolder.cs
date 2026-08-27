namespace LinuxCompat.Platform;

/// <summary>
/// The single source of truth for the game's data folder on Linux.
///
/// The platform factory builds <c>VRagePlatformCore.AppDataPath</c> once the engine exists,
/// while the native library resolver needs its wrapper cache as the game's assemblies load.
/// Both derive the root here so saves, logs, and caches stay under the same data folder.
///
/// The root is <c>~/.config/SpaceEngineers2</c>, deliberately not the XDG data directory this
/// port used before: it puts the game beside <c>~/.config/Pulsar</c>, which loads it.
///
/// <see cref="Environment.SpecialFolder.ApplicationData"/> is what locates it, matching how
/// Pulsar finds its own folder, so a user who moves their configuration home moves both
/// together. <see cref="Environment.SpecialFolderOption.DoNotVerify"/> is required rather
/// than cosmetic: the default option verifies that the directory exists and returns an empty
/// string when it does not, which would silently turn the root into a path relative to the
/// current directory — that is, into the game's installation folder.
/// </summary>
internal static class LinuxDataFolder
{
    /// <summary>
    /// The folder name under the configuration home. This is <c>VRageCore.ApplicationName</c>,
    /// hard coded because the native library resolver runs before <c>VRageCore</c> exists.
    /// </summary>
    private const string ApplicationName = "SpaceEngineers2";

    private const string AppDataArgument = "-appData:";

    /// <summary>
    /// The game's data folder, holding <c>AppData</c> (SaveGames, Blueprints, EngineOptions)
    /// and <c>Temp</c> (Logs, CrashReports, ShaderCache), plus the caches this
    /// plugin keeps beside them.
    ///
    /// This is the default location. The game's <c>-appData:</c> argument overrides it for
    /// everything the engine stores, via <see cref="Resolve"/>, but not for this plugin's own
    /// caches, which belong to the installation rather than to a data profile.
    /// </summary>
    public static string Root { get; } = Path.Combine(ConfigurationHome(), ApplicationName);

    /// <summary>
    /// The effective data folder for engine data, honouring the game's
    /// <c>-appData:&lt;path&gt;</c> argument, which overrides <see cref="Root"/>.
    /// </summary>
    /// <param name="customUserDataPath">
    /// The path the engine already parsed out of the command line, or null for callers that
    /// run too early to have one. Those callers read the raw command line instead.
    /// </param>
    public static string Resolve(string? customUserDataPath = null) =>
        customUserDataPath is { Length: > 0 } path ? path : CommandLineAppDataPath() ?? Root;

    /// <summary>
    /// The <c>-appData:</c> path from the raw command line, for callers that run before the
    /// engine has parsed its arguments. Null when the argument is absent.
    /// </summary>
    private static string? CommandLineAppDataPath()
    {
        foreach (string argument in Environment.GetCommandLineArgs())
        {
            if (argument.StartsWith(AppDataArgument, StringComparison.Ordinal))
                return argument[AppDataArgument.Length..];
        }

        return null;
    }

    /// <summary>
    /// The user's configuration home. Falls back to <c>$HOME/.config</c> if the special folder
    /// cannot be resolved at all, so that the root is never a relative path.
    /// </summary>
    private static string ConfigurationHome()
    {
        string configurationHome = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.DoNotVerify
        );

        return configurationHome is { Length: > 0 }
            ? configurationHome
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config"
            );
    }
}
