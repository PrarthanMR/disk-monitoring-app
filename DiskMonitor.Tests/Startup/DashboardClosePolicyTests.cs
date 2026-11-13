using DiskMonitor.App.Lifecycle;
using System.Windows.Forms;

namespace DiskMonitor.Tests.Startup;

public sealed class DashboardClosePolicyTests
{
    [Fact]
    public void UserClosingWindowHidesItInTray()
    {
        Assert.True(DashboardClosePolicy.ShouldHide(CloseReason.UserClosing, explicitExit: false));
    }

    [Theory]
    [InlineData(CloseReason.WindowsShutDown)]
    [InlineData(CloseReason.ApplicationExitCall)]
    public void SystemShutdownIsNotConvertedToHide(CloseReason closeReason)
    {
        Assert.False(DashboardClosePolicy.ShouldHide(closeReason, explicitExit: false));
    }

    [Fact]
    public void ExplicitExitIsNotConvertedToHide()
    {
        Assert.False(DashboardClosePolicy.ShouldHide(CloseReason.UserClosing, explicitExit: true));
    }
}
