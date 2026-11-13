namespace DiskMonitor.App.Notifications;

public sealed class BalloonNotificationService : INotificationService
{
    private readonly NotifyIcon _notifyIcon;

    public BalloonNotificationService(NotifyIcon notifyIcon)
    {
        ArgumentNullException.ThrowIfNull(notifyIcon);
        _notifyIcon = notifyIcon;
    }

    public void Show(string title, string message, ToolTipIcon icon)
    {
        _notifyIcon.ShowBalloonTip(5000, Limit(title, 63), Limit(message, 255), icon);
    }

    private static string Limit(string text, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= maxLength ? text : $"{text[..(maxLength - 1)]}…";
    }
}
