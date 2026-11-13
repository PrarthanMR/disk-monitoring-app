namespace DiskMonitor.Windows.Startup;

/// <summary>Registers the application for startup for the current user without elevation.</summary>
public sealed class WindowsStartupRegistration
{
    private const string EntryName = "DiskMonitor";
    private readonly string _executablePath;
    private readonly IStartupEntryStore _entryStore;

    /// <summary>Creates a registration service for the running application.</summary>
    public WindowsStartupRegistration()
        : this(Environment.ProcessPath
            ?? throw new InvalidOperationException("The application executable path is unavailable."),
            new RegistryStartupEntryStore())
    {
    }

    /// <summary>Creates a registration service with an explicit executable path and entry store.</summary>
    public WindowsStartupRegistration(string executablePath, IStartupEntryStore entryStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(entryStore);
        _executablePath = Path.GetFullPath(executablePath);
        _entryStore = entryStore;
    }

    /// <summary>Gets whether the current-user startup entry points to this executable.</summary>
    public bool IsEnabled =>
        string.Equals(_entryStore.GetCommand(EntryName), BuildCommand(_executablePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>Enables or disables current-user startup registration idempotently.</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            string command = BuildCommand(_executablePath);
            if (!string.Equals(_entryStore.GetCommand(EntryName), command, StringComparison.OrdinalIgnoreCase))
            {
                _entryStore.SetCommand(EntryName, command);
            }

            return;
        }

        _entryStore.RemoveCommand(EntryName);
    }

    private static string BuildCommand(string executablePath) => $"\"{executablePath}\"";
}
