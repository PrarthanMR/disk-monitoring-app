using DiskMonitor.Windows.Startup;

namespace DiskMonitor.Tests.Startup;

public sealed class WindowsStartupRegistrationTests
{
    [Fact]
    public void SetEnabledRegistersCurrentUserCommandOnlyOnceAndCanRemoveIt()
    {
        var store = new InMemoryStartupEntryStore();
        var registration = new WindowsStartupRegistration(
            @"C:\Program Files\Disk Monitor\DiskMonitor.App.exe",
            store);

        registration.SetEnabled(true);
        registration.SetEnabled(true);

        Assert.True(registration.IsEnabled);
        Assert.Equal(1, store.WriteCount);
        Assert.Equal(
            "\"C:\\Program Files\\Disk Monitor\\DiskMonitor.App.exe\"",
            store.GetCommand("DiskMonitor"));

        registration.SetEnabled(false);

        Assert.False(registration.IsEnabled);
        Assert.Equal(1, store.RemoveCount);
    }

    [Fact]
    public void ExistingDifferentCommandIsReplacedWhenEnabled()
    {
        var store = new InMemoryStartupEntryStore();
        store.SetCommand("DiskMonitor", "\"C:\\Old\\DiskMonitor.exe\"");
        var registration = new WindowsStartupRegistration(@"C:\New\DiskMonitor.exe", store);

        registration.SetEnabled(true);

        Assert.True(registration.IsEnabled);
        Assert.Equal("\"C:\\New\\DiskMonitor.exe\"", store.GetCommand("DiskMonitor"));
        Assert.Equal(2, store.WriteCount);
    }

    private sealed class InMemoryStartupEntryStore : IStartupEntryStore
    {
        private readonly Dictionary<string, string> _commands = new(StringComparer.OrdinalIgnoreCase);

        public int WriteCount { get; private set; }
        public int RemoveCount { get; private set; }

        public string? GetCommand(string valueName) =>
            _commands.TryGetValue(valueName, out string? command) ? command : null;

        public void SetCommand(string valueName, string command)
        {
            WriteCount++;
            _commands[valueName] = command;
        }

        public void RemoveCommand(string valueName)
        {
            RemoveCount++;
            _commands.Remove(valueName);
        }
    }
}
