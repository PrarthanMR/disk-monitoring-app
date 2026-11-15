namespace DiskMonitor.Core.Models;

public sealed record ReadinessUpdate(
    DateTimeOffset TimestampUtc,
    ReadinessState State,
    double? ActiveTimePercent,
    TimeSpan StableElapsed,
    string Explanation);
