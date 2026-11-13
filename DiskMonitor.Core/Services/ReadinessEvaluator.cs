using DiskMonitor.Core.Configuration;
using DiskMonitor.Core.Models;

namespace DiskMonitor.Core.Services;

/// <summary>Assesses startup readiness from a minimum baseline and recent rolling disk measurements.</summary>
public sealed class ReadinessEvaluator
{
    private readonly MonitoringThresholds _thresholds;
    private readonly HealthEvaluator _healthEvaluator;
    private readonly List<DiskMetrics> _recentMeasurements = [];
    private DateTimeOffset? _baselineStartUtc;
    private DateTimeOffset? _lastValidTimestampUtc;
    private DateTimeOffset? _lastTimestampUtc;

    /// <summary>Creates an evaluator using the supplied thresholds or the defaults.</summary>
    public ReadinessEvaluator(MonitoringThresholds? thresholds = null)
    {
        _thresholds = thresholds ?? new MonitoringThresholds();
        _thresholds.Validate();
        _healthEvaluator = new HealthEvaluator(_thresholds);
    }

    /// <summary>Adds a measurement and returns the current startup-readiness assessment.</summary>
    public SystemReadiness Evaluate(DiskMetrics measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        if (measurement.TimestampUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Measurement timestamps must be UTC.", nameof(measurement));
        }

        if (_lastTimestampUtc is DateTimeOffset previous && measurement.TimestampUtc <= previous)
        {
            throw new ArgumentException("Measurements must have strictly increasing timestamps.", nameof(measurement));
        }

        _lastTimestampUtc = measurement.TimestampUtc;
        _healthEvaluator.Evaluate(measurement);
        _recentMeasurements.Add(measurement);
        TrimRecentMeasurements(measurement.TimestampUtc);

        if (TryGetRequiredMetrics(measurement, out _, out _))
        {
            if (_lastValidTimestampUtc is not DateTimeOffset lastValid
                || measurement.TimestampUtc - lastValid > _thresholds.SamplingInterval + _thresholds.SamplingInterval)
            {
                _baselineStartUtc = measurement.TimestampUtc;
            }

            _lastValidTimestampUtc = measurement.TimestampUtc;
        }

        TimeSpan baselineDuration = _baselineStartUtc is DateTimeOffset baselineStart
            && _lastValidTimestampUtc is DateTimeOffset lastValidTimestamp
                ? lastValidTimestamp - baselineStart
                : TimeSpan.Zero;
        if (baselineDuration < _thresholds.ReadinessBaselineDuration)
        {
            return CreateReadiness(
                measurement,
                SystemReadinessState.Initializing,
                $"Collecting startup baseline; {Remaining(_thresholds.ReadinessBaselineDuration - baselineDuration)} remain before readiness can be assessed.",
                baselineDuration);
        }

        if (!TryGetRecentAverages(measurement.TimestampUtc, out double activeAverage, out double responseAverage, out string reason))
        {
            return CreateReadiness(measurement, SystemReadinessState.Wait, reason, baselineDuration);
        }

        if (_healthEvaluator.CurrentState is DiskHealthState.Warning or DiskHealthState.Critical)
        {
            string healthExplanation = _healthEvaluator.CurrentState == DiskHealthState.Critical
                ? "Sustained critical disk latency or combined high activity and latency is present."
                : "Disk activity has remained elevated long enough to trigger a health warning.";
            return CreateReadiness(measurement, SystemReadinessState.Degraded, healthExplanation, baselineDuration);
        }

        if (_healthEvaluator.CurrentState == DiskHealthState.Recovery)
        {
            return CreateReadiness(
                measurement,
                SystemReadinessState.Wait,
                "Disk health is recovering; wait for stable measurements before proceeding.",
                baselineDuration);
        }

        if (activeAverage >= _thresholds.WarningActiveTimePercent
            || responseAverage >= _thresholds.ElevatedResponseTimeMilliseconds)
        {
            return CreateReadiness(
                measurement,
                SystemReadinessState.Wait,
                $"Recent average activity ({activeAverage:F1}%) or response time ({responseAverage:F1} ms) is elevated; waiting for a stable window.",
                baselineDuration);
        }

        return CreateReadiness(
            measurement,
            SystemReadinessState.Ready,
            $"A {_thresholds.ReadinessBaselineDuration.TotalSeconds:F0}-second baseline and recent stable disk activity and latency are available.",
            baselineDuration);
    }

    private bool TryGetRecentAverages(
        DateTimeOffset now,
        out double activeAverage,
        out double responseAverage,
        out string reason)
    {
        activeAverage = default;
        responseAverage = default;
        DateTimeOffset start = now - _thresholds.ReadinessRecentWindow;
        DiskMetrics[] window = _recentMeasurements
            .Where(sample => sample.TimestampUtc >= start && sample.TimestampUtc <= now)
            .ToArray();
        if (window.Length < 2
            || window[0].TimestampUtc > start + _thresholds.SamplingInterval
            || now - window[^1].TimestampUtc > _thresholds.SamplingInterval + _thresholds.SamplingInterval)
        {
            reason = "Waiting for a complete recent measurement window before assessing readiness.";
            return false;
        }

        var activeValues = new List<double>(window.Length);
        var responseValues = new List<double>(window.Length);
        foreach (DiskMetrics sample in window)
        {
            if (!TryGetRequiredMetrics(sample, out double active, out double response))
            {
                reason = sample.UnavailableMetrics.TryGetValue("Disk active time", out string? activeReason)
                    ? $"Waiting for disk active-time metrics: {activeReason}"
                    : sample.UnavailableMetrics.TryGetValue("Average disk response time", out string? responseReason)
                        ? $"Waiting for disk response-time metrics: {responseReason}"
                        : "Waiting for valid disk activity and response-time metrics.";
                return false;
            }

            activeValues.Add(active);
            responseValues.Add(response);
        }

        activeAverage = activeValues.Average();
        responseAverage = responseValues.Average();
        reason = string.Empty;
        return true;
    }

    private static bool TryGetRequiredMetrics(DiskMetrics measurement, out double active, out double response)
    {
        active = measurement.ActiveTimePercent.GetValueOrDefault();
        response = measurement.AverageResponseTimeMilliseconds.GetValueOrDefault();
        return measurement.ActiveTimePercent is double activeValue
                && double.IsFinite(activeValue)
                && activeValue is >= 0 and <= 100
            && measurement.AverageResponseTimeMilliseconds is double responseValue
                && double.IsFinite(responseValue)
                && responseValue >= 0;
    }

    private void TrimRecentMeasurements(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - _thresholds.ReadinessRecentWindow - _thresholds.SamplingInterval;
        _recentMeasurements.RemoveAll(sample => sample.TimestampUtc < cutoff);
    }

    private static string Remaining(TimeSpan duration) =>
        $"{Math.Ceiling(duration.TotalSeconds):F0} seconds";

    private static SystemReadiness CreateReadiness(
        DiskMetrics measurement,
        SystemReadinessState state,
        string explanation,
        TimeSpan baselineDuration) =>
        new(measurement.TimestampUtc, state, explanation, baselineDuration);
}
