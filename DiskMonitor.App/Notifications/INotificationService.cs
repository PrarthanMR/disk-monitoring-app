namespace DiskMonitor.App.Notifications;

/// <summary>Delivers user notifications independently of the notification mechanism.</summary>
public interface INotificationService
{
    /// <summary>Displays a short notification.</summary>
    void Show(string title, string message, ToolTipIcon icon);
}
