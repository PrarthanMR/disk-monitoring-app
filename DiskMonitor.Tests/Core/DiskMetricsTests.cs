using DiskMonitor.Core.Models;

namespace DiskMonitor.Tests.Core;

public sealed class DiskMetricsTests
{
    [Fact]
    public void UnavailablePerformanceMetricsRemainNullAndVolumeSpaceStaysSeparate()
    {
        DateTimeOffset timestampUtc = DateTimeOffset.UtcNow;
        var space = new DiskSpaceMetrics(@"C:\", 1_000, 250);
        var unavailable = new Dictionary<string, string>
        {
            ["Disk read throughput"] = "Counter is unavailable."
        };

        var metrics = new DiskMetrics(
            timestampUtc,
            ActiveTimePercent: 12.5,
            ReadThroughputMBps: null,
            WriteThroughputMBps: 3.25,
            AverageResponseTimeMilliseconds: 1.5,
            CurrentDiskQueueLength: 0,
            SystemVolumeSpace: space,
            UnavailableMetrics: unavailable);

        Assert.Equal(timestampUtc, metrics.TimestampUtc);
        Assert.Null(metrics.ReadThroughputMBps);
        Assert.Equal("Counter is unavailable.", metrics.UnavailableMetrics["Disk read throughput"]);
        Assert.Same(space, metrics.SystemVolumeSpace);
        Assert.Equal(12.5, metrics.ActiveTimePercent);
    }
}
