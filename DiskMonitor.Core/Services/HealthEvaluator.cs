using DiskMonitor.Core.Configuration;
using DiskMonitor.Core.Models;

namespace DiskMonitor.Core.Services;

/// <summary>
/// Evaluates rolling disk measurements and emits alerts only when health state changes.
/// </summary>
public sealed class HealthEvaluator
{
    private readonly MonitoringThresholds _thresholds;
    private readonly List<DiskMetrics> _measurements = [];
    private DiskHealthState _state = DiskHealthState.InsufficientData;
    private DiskHealthState? _incidentState;
    private DiskHealthState? _notifiedIncidentState;
    private DateTimeOffset? _lastTimestampUtc;

    /// <summary>Creates an evaluator using the supplied thresholds or the defaults.</summary>
    public HealthEvaluator(MonitoringThresholds? thresholds = null)
    {
        _thresholds = thresholds ?? new MonitoringThresholds();
        _thresholds.Validate();
    }

    /// <summary>The latest evaluated state.</summary>
    public DiskHealthState CurrentState => _state;

    /// <summary>
    /// Adds a measurement, updates the current state, and returns an alert only on a state transition.
    /// </summary>
    public DiskAlert? Evaluate(DiskMetrics measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        ValidateTimestamp(measurement.TimestampUtc);
        _lastTimestampUtc = measurement.TimestampUtc;
        _measurements.Add(measurement);
        TrimMeasurements(measurement.TimestampUtc);

        if (!TryGetRequiredMetrics(measurement, out double activeTime, out double responseTime, out string unavailableReason))
        {
            return TransitionTo(DiskHealthState.Unavailable, measurement, unavailableReason);
        }

        if (_measurements.Count < 2
            || measurement.TimestampUtc - _measurements[0].TimestampUtc < _thresholds.SamplingInterval)
        {
            return TransitionTo(
                DiskHealthState.InsufficientData,
                measurement,
                "At least two spaced measurements are required to evaluate disk health.");
        }

        bool criticalLatency = IsSustained(
            sample => HasRequiredMetrics(sample, out _, out double response)
                && response >= _thresholds.CriticalResponseTimeMilliseconds,
            _thresholds.CriticalResponseDuration,
            measurement.TimestampUtc);
        bool criticalActivityAndLatency = IsSustained(
            sample => HasRequiredMetrics(sample, out double active, out double response)
                && active >= _thresholds.CriticalActiveTimePercent
                && response >= _thresholds.ElevatedResponseTimeMilliseconds,
            _thresholds.CriticalActiveDuration,
            measurement.TimestampUtc);
        bool warningActivity = IsSustained(
            sample => HasRequiredMetrics(sample, out double active, out _)
                && active >= _thresholds.WarningActiveTimePercent,
            _thresholds.WarningActiveDuration,
            measurement.TimestampUtc);
        bool recovery = IsSustained(
            sample => HasRequiredMetrics(sample, out double active, out double response)
                && active < _thresholds.RecoveryActiveTimePercent
                && response < _thresholds.RecoveryResponseTimeMilliseconds,
            _thresholds.RecoveryDuration,
            measurement.TimestampUtc);

        if (_incidentState is not null)
        {
            if (recovery)
            {
                _incidentState = null;
                _notifiedIncidentState = null;
                return TransitionTo(
                    DiskHealthState.Recovery,
                    measurement,
                    $"Disk activity remained below {_thresholds.RecoveryActiveTimePercent:F0}% with response time below {_thresholds.RecoveryResponseTimeMilliseconds:F0} ms for {_thresholds.RecoveryDuration.TotalSeconds:F0} seconds.");
            }

            if (criticalLatency || criticalActivityAndLatency)
            {
                _incidentState = DiskHealthState.Critical;
            }

            string incidentExplanation = _incidentState == DiskHealthState.Critical
                ? BuildCriticalExplanation(activeTime, responseTime, criticalLatency)
                : BuildWarningExplanation(activeTime);
            return TransitionToIncident(_incidentState.Value, measurement, incidentExplanation);
        }

        if (criticalLatency || criticalActivityAndLatency)
        {
            _incidentState = DiskHealthState.Critical;
            return TransitionToIncident(
                DiskHealthState.Critical,
                measurement,
                BuildCriticalExplanation(activeTime, responseTime, criticalLatency));
        }

        if (warningActivity)
        {
            _incidentState = DiskHealthState.Warning;
            return TransitionToIncident(
                DiskHealthState.Warning,
                measurement,
                BuildWarningExplanation(activeTime));
        }

        if (_state == DiskHealthState.Recovery)
        {
            return TransitionTo(
                DiskHealthState.Normal,
                measurement,
                "Disk measurements remain below alert thresholds after recovery.");
        }

        return TransitionTo(
            DiskHealthState.Normal,
            measurement,
            "Recent disk activity and response time are within configured thresholds.");
    }

    private bool IsSustained(
        Func<DiskMetrics, bool> condition,
        TimeSpan duration,
        DateTimeOffset now)
    {
        DateTimeOffset start = now - duration;
        DiskMetrics[] window = _measurements
            .Where(sample => sample.TimestampUtc >= start && sample.TimestampUtc <= now)
            .ToArray();

        if (window.Length < 2 || window[0].TimestampUtc > start + _thresholds.SamplingInterval)
        {
            return false;
        }

        DateTimeOffset previous = window[0].TimestampUtc;
        foreach (DiskMetrics sample in window)
        {
            if (!condition(sample)
                || sample.TimestampUtc - previous > _thresholds.SamplingInterval + _thresholds.SamplingInterval)
            {
                return false;
            }

            previous = sample.TimestampUtc;
        }

        return now - previous <= _thresholds.SamplingInterval + _thresholds.SamplingInterval
            && previous - window[0].TimestampUtc >= duration;
    }

    private bool TryGetRequiredMetrics(
        DiskMetrics measurement,
        out double activeTime,
        out double responseTime,
        out string reason)
    {
        activeTime = default;
        responseTime = default;
        if (!TryGetActiveTime(measurement, out activeTime))
        {
            reason = GetUnavailableReason(measurement, "Disk active time", "Active-time metric is missing or invalid.");
            return false;
        }

        if (!TryGetResponseTime(measurement, out responseTime))
        {
            reason = GetUnavailableReason(measurement, "Average disk response time", "Response-time metric is missing or invalid.");
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool HasRequiredMetrics(DiskMetrics measurement, out double activeTime, out double responseTime)
    {
        responseTime = default;
        return TryGetActiveTime(measurement, out activeTime)
            && TryGetResponseTime(measurement, out responseTime);
    }

    private static bool TryGetActiveTime(DiskMetrics measurement, out double value)
    {
        value = measurement.ActiveTimePercent.GetValueOrDefault();
        return measurement.ActiveTimePercent is double active && double.IsFinite(active) && active is >= 0 and <= 100;
    }

    private static bool TryGetResponseTime(DiskMetrics measurement, out double value)
    {
        value = measurement.AverageResponseTimeMilliseconds.GetValueOrDefault();
        return measurement.AverageResponseTimeMilliseconds is double response
            && double.IsFinite(response)
            && response >= 0;
    }

    private static string GetUnavailableReason(DiskMetrics measurement, string metricName, string fallback) =>
        measurement.UnavailableMetrics.TryGetValue(metricName, out string? reason) ? reason : fallback;

    private DiskAlert? TransitionTo(DiskHealthState nextState, DiskMetrics measurement, string explanation)
    {
        if (_state == nextState)
        {
            return null;
        }

        _state = nextState;
        return new DiskAlert(
            measurement.TimestampUtc,
            nextState,
            explanation,
            measurement.ActiveTimePercent,
            measurement.AverageResponseTimeMilliseconds);
    }

    private DiskAlert? TransitionToIncident(
        DiskHealthState incidentState,
        DiskMetrics measurement,
        string explanation)
    {
        if (_state == incidentState)
        {
            return null;
        }

        _state = incidentState;
        if (_notifiedIncidentState == incidentState)
        {
            return null;
        }

        _notifiedIncidentState = incidentState;
        return new DiskAlert(
            measurement.TimestampUtc,
            incidentState,
            explanation,
            measurement.ActiveTimePercent,
            measurement.AverageResponseTimeMilliseconds);
    }

    private string BuildWarningExplanation(double activeTime) =>
        $"Disk active time reached {activeTime:F1}% and remained at or above {_thresholds.WarningActiveTimePercent:F0}% for {_thresholds.WarningActiveDuration.TotalSeconds:F0} seconds. Activity alone does not indicate disk failure.";

    private string BuildCriticalExplanation(double activeTime, double responseTime, bool criticalLatency) =>
        criticalLatency
            ? $"Average disk response time was {responseTime:F1} ms or higher for {_thresholds.CriticalResponseDuration.TotalSeconds:F0} seconds."
            : $"Disk active time was {activeTime:F1}% or higher with response time of at least {_thresholds.ElevatedResponseTimeMilliseconds:F0} ms for {_thresholds.CriticalActiveDuration.TotalSeconds:F0} seconds.";

    private void ValidateTimestamp(DateTimeOffset timestampUtc)
    {
        if (timestampUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Measurement timestamps must be UTC.", nameof(timestampUtc));
        }

        if (_lastTimestampUtc is DateTimeOffset previous && timestampUtc <= previous)
        {
            throw new ArgumentException("Measurements must have strictly increasing timestamps.", nameof(timestampUtc));
        }
    }

    private void TrimMeasurements(DateTimeOffset now)
    {
        TimeSpan maxDuration = new[]
        {
            _thresholds.WarningActiveDuration,
            _thresholds.CriticalActiveDuration,
            _thresholds.CriticalResponseDuration,
            _thresholds.RecoveryDuration
        }.Max();
        DateTimeOffset cutoff = now - maxDuration - _thresholds.SamplingInterval;
        _measurements.RemoveAll(sample => sample.TimestampUtc < cutoff);
    }
}
