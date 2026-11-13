namespace DiskMonitor.Core.Configuration;

/// <summary>
/// Configurable thresholds for disk health and startup-readiness evaluation.
/// </summary>
public sealed record MonitoringThresholds
{
    /// <summary>Interval between expected measurements.</summary>
    public TimeSpan SamplingInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Active-time percentage at which sustained activity becomes a warning.</summary>
    public double WarningActiveTimePercent { get; init; } = 90;

    /// <summary>Duration for which warning-level active time must persist.</summary>
    public TimeSpan WarningActiveDuration { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Active-time percentage used with elevated latency for a critical state.</summary>
    public double CriticalActiveTimePercent { get; init; } = 95;

    /// <summary>Response time considered elevated when active time is critically high.</summary>
    public double ElevatedResponseTimeMilliseconds { get; init; } = 50;

    /// <summary>Duration for which critically high activity and elevated latency must persist.</summary>
    public TimeSpan CriticalActiveDuration { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Response-time threshold at which sustained latency becomes critical.</summary>
    public double CriticalResponseTimeMilliseconds { get; init; } = 100;

    /// <summary>Duration for which critical response time must persist.</summary>
    public TimeSpan CriticalResponseDuration { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Active time below which recovery may be established.</summary>
    public double RecoveryActiveTimePercent { get; init; } = 70;

    /// <summary>Maximum response time that qualifies as recovered.</summary>
    public double RecoveryResponseTimeMilliseconds { get; init; } = 50;

    /// <summary>Duration for which recovery conditions must persist.</summary>
    public TimeSpan RecoveryDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Minimum time span required for a startup baseline.</summary>
    public TimeSpan ReadinessBaselineDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Recent rolling window used to evaluate startup readiness.</summary>
    public TimeSpan ReadinessRecentWindow { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Validates that thresholds are finite, ordered, and have positive durations.</summary>
    public void Validate()
    {
        if (SamplingInterval <= TimeSpan.Zero
            || WarningActiveDuration <= TimeSpan.Zero
            || CriticalActiveDuration <= TimeSpan.Zero
            || CriticalResponseDuration <= TimeSpan.Zero
            || RecoveryDuration <= TimeSpan.Zero
            || ReadinessBaselineDuration <= TimeSpan.Zero
            || ReadinessRecentWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MonitoringThresholds), "Threshold durations must be positive.");
        }

        if (!IsPercentage(WarningActiveTimePercent)
            || !IsPercentage(CriticalActiveTimePercent)
            || !IsPercentage(RecoveryActiveTimePercent)
            || RecoveryActiveTimePercent >= WarningActiveTimePercent
            || WarningActiveTimePercent >= CriticalActiveTimePercent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MonitoringThresholds),
                "Active-time thresholds must be percentages ordered recovery < warning < critical.");
        }

        if (!IsNonNegative(ElevatedResponseTimeMilliseconds)
            || !IsNonNegative(RecoveryResponseTimeMilliseconds)
            || !IsNonNegative(CriticalResponseTimeMilliseconds)
            || RecoveryResponseTimeMilliseconds > ElevatedResponseTimeMilliseconds
            || ElevatedResponseTimeMilliseconds >= CriticalResponseTimeMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MonitoringThresholds),
                "Response-time thresholds must be non-negative and ordered recovery <= elevated < critical.");
        }
    }

    private static bool IsPercentage(double value) => double.IsFinite(value) && value is >= 0 and <= 100;

    private static bool IsNonNegative(double value) => double.IsFinite(value) && value >= 0;
}
