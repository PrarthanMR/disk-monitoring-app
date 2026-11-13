namespace DiskMonitor.Core.Models;

/// <summary>
/// A point-in-time snapshot of physical-disk performance and separate volume-space information.
/// A null performance value means that the corresponding counter was unavailable.
/// </summary>
/// <param name="TimestampUtc">The UTC time at which this sample was collected.</param>
/// <param name="ActiveTimePercent">Disk active time, calculated as 100 minus % Idle Time.</param>
/// <param name="ReadThroughputMBps">Read throughput in decimal megabytes per second.</param>
/// <param name="WriteThroughputMBps">Write throughput in decimal megabytes per second.</param>
/// <param name="AverageResponseTimeMilliseconds">Average transfer response time in milliseconds.</param>
/// <param name="CurrentDiskQueueLength">The current physical-disk queue length.</param>
/// <param name="SystemVolumeSpace">Capacity details for the system volume, when available.</param>
/// <param name="UnavailableMetrics">Reasons for metrics that could not be collected.</param>
public sealed record DiskMetrics(
    DateTimeOffset TimestampUtc,
    double? ActiveTimePercent,
    double? ReadThroughputMBps,
    double? WriteThroughputMBps,
    double? AverageResponseTimeMilliseconds,
    double? CurrentDiskQueueLength,
    DiskSpaceMetrics? SystemVolumeSpace,
    IReadOnlyDictionary<string, string> UnavailableMetrics);
