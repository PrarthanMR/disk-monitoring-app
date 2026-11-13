namespace DiskMonitor.Core.Models;

/// <summary>The startup-readiness evaluation state.</summary>
public enum SystemReadinessState
{
    /// <summary>The minimum startup baseline has not yet been collected.</summary>
    Initializing,

    /// <summary>More recent healthy measurements are needed before proceeding.</summary>
    Wait,

    /// <summary>Recent disk activity or latency is sustained at a concerning level.</summary>
    Degraded,

    /// <summary>A sufficient baseline and a recent healthy measurement window are available.</summary>
    Ready
}

/// <summary>A timestamped startup-readiness assessment with its human-readable explanation.</summary>
/// <param name="TimestampUtc">The timestamp of the latest measurement.</param>
/// <param name="State">The readiness state.</param>
/// <param name="Explanation">Why the system is in this readiness state.</param>
/// <param name="BaselineDuration">The amount of startup baseline collected.</param>
public sealed record SystemReadiness(
    DateTimeOffset TimestampUtc,
    SystemReadinessState State,
    string Explanation,
    TimeSpan BaselineDuration);
