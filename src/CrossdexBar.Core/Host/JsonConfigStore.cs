using System.Text.Json;

namespace CrossdexBar.Core.Host;

public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IPlatformPaths _paths;

    public JsonConfigStore(IPlatformPaths paths) => _paths = paths;

    public AppConfig Load()
    {
        if (!File.Exists(_paths.ConfigFilePath))
            return new AppConfig();

        var json = File.ReadAllText(_paths.ConfigFilePath);
        return JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions) ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(_paths.ConfigDirectory);
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        File.WriteAllText(_paths.ConfigFilePath, json);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_paths.ConfigFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
