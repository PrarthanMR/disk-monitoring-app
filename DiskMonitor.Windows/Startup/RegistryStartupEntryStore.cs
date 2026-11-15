using Microsoft.Win32;

namespace DiskMonitor.Windows.Startup;

/// <summary>Stores startup commands in the current user's Windows Run key.</summary>
public sealed class RegistryStartupEntryStore : IStartupEntryStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <inheritdoc />
    public string? GetCommand(string valueName)
    {
        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return runKey?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    /// <inheritdoc />
    public void SetCommand(string valueName, string command)
    {
        using RegistryKey runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("The current-user startup registry key could not be opened.");
        runKey.SetValue(valueName, command, RegistryValueKind.String);
    }

    /// <inheritdoc />
    public void RemoveCommand(string valueName)
    {
        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        runKey?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
