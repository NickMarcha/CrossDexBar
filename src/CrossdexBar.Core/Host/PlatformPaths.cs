namespace CrossdexBar.Core.Host;

public sealed class PlatformPaths : IPlatformPaths
{
    public string HomeDirectory { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string ConfigDirectory { get; }
    public string ConfigFilePath => Path.Combine(ConfigDirectory, "config.json");
    public string AppDataRoot { get; }

    public PlatformPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            AppDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            ConfigDirectory = Path.Combine(AppDataRoot, "CrossdexBar");
        }
        else
        {
            var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            AppDataRoot = string.IsNullOrEmpty(xdgConfigHome) ? Path.Combine(HomeDirectory, ".config") : xdgConfigHome;
            ConfigDirectory = Path.Combine(AppDataRoot, "crossdexbar");
        }
    }
}
