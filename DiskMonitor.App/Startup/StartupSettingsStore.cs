using System.Text.Json;

namespace DiskMonitor.App.Startup;

public sealed class StartupSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
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

    public StartupSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new StartupSettings();
        }

        return JsonSerializer.Deserialize<StartupSettings>(File.ReadAllText(_settingsPath))
            ?? throw new InvalidDataException("The Disk Monitor settings file does not contain valid settings.");
    }

    public void Save(StartupSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("The settings file path must include a directory.");
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
