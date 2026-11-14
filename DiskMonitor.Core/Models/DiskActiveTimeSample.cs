namespace DiskMonitor.Core.Models;

public sealed record DiskActiveTimeSample(
    DateTimeOffset TimestampUtc,
    double? ActiveTimePercent,
    string? UnavailableReason);
