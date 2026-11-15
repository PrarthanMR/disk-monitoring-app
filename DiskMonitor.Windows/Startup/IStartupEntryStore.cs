namespace DiskMonitor.Windows.Startup;

/// <summary>Reads and writes a single named current-user startup command.</summary>
public interface IStartupEntryStore
{
    /// <summary>Gets the startup command for a value name, or null when it is absent.</summary>
    string? GetCommand(string valueName);

    /// <summary>Creates or replaces a startup command for the current user.</summary>
    void SetCommand(string valueName, string command);

    /// <summary>Removes a startup command for the current user if it exists.</summary>
    void RemoveCommand(string valueName);
}
