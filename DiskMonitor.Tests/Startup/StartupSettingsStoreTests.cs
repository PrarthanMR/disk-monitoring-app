using DiskMonitor.App.Startup;

namespace DiskMonitor.Tests.Startup;

public sealed class StartupSettingsStoreTests
{
    [Fact]
    public void MissingSettingsDefaultToStartupDisabledAndSaveCanBeReloaded()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"DiskMonitor.Tests.{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(directory, "settings.json");

        try
        {
            var store = new StartupSettingsStore(settingsPath);
            Assert.False(store.Load().StartWithWindows);

            var settings = new StartupSettings(StartWithWindows: true);
            store.Save(settings);

            Assert.Equal(settings, store.Load());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
