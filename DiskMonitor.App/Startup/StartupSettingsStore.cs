using System.Text.Json;

namespace DiskMonitor.App.Startup;

public sealed class StartupSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private readonly string _settingsPath;

    public StartupSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DiskMonitor",
            "settings.json"))
    {
    }

    public StartupSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = settingsPath;
    }

    public ReadinessSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new ReadinessSettings();
        }

        ReadinessSettings settings = JsonSerializer.Deserialize<ReadinessSettings>(File.ReadAllText(_settingsPath), JsonOptions)
            ?? throw new InvalidDataException("The Disk Monitor settings file does not contain valid settings.");
        settings.Validate();
        return settings;
    }

    public void Save(ReadinessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        string? directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("The settings file path must include a directory.");
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
