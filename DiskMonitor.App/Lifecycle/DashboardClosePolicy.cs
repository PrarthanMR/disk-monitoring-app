namespace DiskMonitor.App.Lifecycle;

internal static class DashboardClosePolicy
{
    public static bool ShouldHide(CloseReason closeReason, bool explicitExit) =>
        !explicitExit
        && closeReason is not (CloseReason.WindowsShutDown or CloseReason.ApplicationExitCall);
}
