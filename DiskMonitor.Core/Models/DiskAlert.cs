namespace DiskMonitor.Core.Models;

/// <summary>A human-readable health-state transition produced by the evaluator.</summary>
/// <param name="TimestampUtc">The timestamp of the measurement that caused the transition.</param>
/// <param name="State">The new health state.</param>
/// <param name="Explanation">Why the state changed.</param>
/// <param name="ActiveTimePercent">The active-time measurement, when available.</param>
/// <param name="AverageResponseTimeMilliseconds">The response-time measurement, when available.</param>
public sealed record DiskAlert(
    DateTimeOffset TimestampUtc,
    DiskHealthState State,
    string Explanation,
    double? ActiveTimePercent,
    double? AverageResponseTimeMilliseconds);
