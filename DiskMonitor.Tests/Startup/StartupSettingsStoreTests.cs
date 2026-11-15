using System.Text.Json;
using DiskMonitor.App.Startup;

namespace DiskMonitor.Tests.Startup;

public sealed class StartupSettingsStoreTests
{
    [Fact]
    public void MissingFileUsesDefaultReadinessSettings()
    {
        string directory = CreateTestDirectory();
        try
        {
            ReadinessSettings settings = new StartupSettingsStore(
                Path.Combine(directory, "settings.json")).Load();

            Assert.Equal(20, settings.ActiveTimeThresholdPercent);
            Assert.Equal(60, settings.StableDurationSeconds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SavesAndReloadsReadinessSettings()
    {
        string directory = CreateTestDirectory();
        try
        {
            string path = Path.Combine(directory, "settings.json");
            var store = new StartupSettingsStore(path);
            var settings = new ReadinessSettings
            {
                ActiveTimeThresholdPercent = 15.5,
                StableDurationSeconds = 90
            };
            store.Save(settings);

            Assert.Equal(settings, store.Load());
            Assert.Contains("\"activeTimeThresholdPercent\": 15.5", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(101, 60)]
    [InlineData(double.NaN, 60)]
    [InlineData(20, 0)]
    [InlineData(20, 3601)]
    public void RejectsInvalidReadinessSettings(double threshold, int duration)
    {
        var settings = new ReadinessSettings
        {
            ActiveTimeThresholdPercent = threshold,
            StableDurationSeconds = duration
        };

        Assert.Throws<InvalidDataException>(settings.Validate);
    }

    [Fact]
    public void LoadingInvalidJsonConfigurationFailsExplicitly()
    {
        string directory = CreateTestDirectory();
        string path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            activeTimeThresholdPercent = 101,
            stableDurationSeconds = 60
        }));

        try
        {
            Assert.Throws<InvalidDataException>(() => new StartupSettingsStore(path).Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"DiskMonitor.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
