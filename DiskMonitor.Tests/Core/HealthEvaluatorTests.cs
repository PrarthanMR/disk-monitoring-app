using DiskMonitor.Core.Models;
using DiskMonitor.Core.Services;

namespace DiskMonitor.Tests.Core;

public sealed class HealthEvaluatorTests
{
    [Fact]
    public void NormalActivityBecomesNormalAfterEnoughMeasurements()
    {
        var evaluator = new HealthEvaluator();

        evaluator.Evaluate(CreateMetrics(0, 20, 5));
        DiskAlert? alert = evaluator.Evaluate(CreateMetrics(1, 25, 6));

        Assert.Equal(DiskHealthState.Normal, evaluator.CurrentState);
        Assert.Equal(DiskHealthState.Normal, alert?.State);
        Assert.False(string.IsNullOrWhiteSpace(alert?.Explanation));
    }

    [Fact]
    public void TemporaryActivitySpikeDoesNotTriggerWarning()
    {
        var evaluator = new HealthEvaluator();
        AddRange(evaluator, 0, 5, 20, 5);
        AddRange(evaluator, 5, 8, 99, 10);
        AddRange(evaluator, 13, 10, 20, 5);

        Assert.Equal(DiskHealthState.Normal, evaluator.CurrentState);
    }

    [Fact]
    public void SustainedHighActivityWithNormalLatencyTriggersWarningNotDiskFailure()
    {
        var evaluator = new HealthEvaluator();
        AddRange(evaluator, 0, 20, 95, 10);

        Assert.Equal(DiskHealthState.Normal, evaluator.CurrentState);

        DiskAlert? alert = evaluator.Evaluate(CreateMetrics(20, 95, 10));

        Assert.Equal(DiskHealthState.Warning, evaluator.CurrentState);
        Assert.Equal(DiskHealthState.Warning, alert?.State);
        Assert.Contains("does not indicate disk failure", alert!.Explanation);
    }

    [Fact]
    public void SustainedHighLatencyTriggersCritical()
    {
        var evaluator = new HealthEvaluator();
        DiskAlert? alert = AddRange(evaluator, 0, 11, 20, 120);

        Assert.Equal(DiskHealthState.Critical, evaluator.CurrentState);
        Assert.Equal(DiskHealthState.Critical, alert?.State);
        Assert.Contains("response time", alert!.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HighActivityAndElevatedLatencyTriggersCritical()
    {
        var evaluator = new HealthEvaluator();
        DiskAlert? alert = AddRange(evaluator, 0, 11, 97, 60);

        Assert.Equal(DiskHealthState.Critical, evaluator.CurrentState);
        Assert.Equal(DiskHealthState.Critical, alert?.State);
    }

    [Fact]
    public void SustainedRecoveryProducesOneRecoveryTransitionThenNormal()
    {
        var evaluator = new HealthEvaluator();
        AddRange(evaluator, 0, 21, 95, 10);
        Assert.Equal(DiskHealthState.Warning, evaluator.CurrentState);

        DiskAlert? recovery = AddRange(evaluator, 21, 31, 30, 5);
        Assert.Equal(DiskHealthState.Recovery, recovery?.State);
        Assert.Equal(DiskHealthState.Recovery, evaluator.CurrentState);

        DiskAlert? normal = evaluator.Evaluate(CreateMetrics(52, 30, 5));
        Assert.Equal(DiskHealthState.Normal, normal?.State);
        Assert.Equal(DiskHealthState.Normal, evaluator.CurrentState);
    }

    [Fact]
    public void MissingRequiredMetricReportsUnavailableWithoutInventingAValue()
    {
        var evaluator = new HealthEvaluator();
        var unavailable = CreateMetrics(0, null, 2, new Dictionary<string, string>
        {
            ["Disk active time"] = "Counter is unavailable."
        });

        DiskAlert? alert = evaluator.Evaluate(unavailable);

        Assert.Equal(DiskHealthState.Unavailable, alert?.State);
        Assert.Null(alert?.ActiveTimePercent);
        Assert.Contains("Counter is unavailable.", alert!.Explanation);
    }

    [Fact]
    public void MissingMetricsDoNotClearAnIncidentOrRepeatItsAlert()
    {
        var evaluator = new HealthEvaluator();
        DiskAlert? incident = AddRange(evaluator, 0, 21, 95, 10);
        Assert.Equal(DiskHealthState.Warning, incident?.State);

        DiskAlert? unavailable = evaluator.Evaluate(CreateMetrics(
            21,
            null,
            10,
            new Dictionary<string, string> { ["Disk active time"] = "Counter is unavailable." }));
        Assert.Equal(DiskHealthState.Unavailable, unavailable?.State);

        DiskAlert? resumed = evaluator.Evaluate(CreateMetrics(22, 30, 10));

        Assert.Null(resumed);
        Assert.Equal(DiskHealthState.Warning, evaluator.CurrentState);
    }

    private static DiskAlert? AddRange(
        HealthEvaluator evaluator,
        int startSeconds,
        int count,
        double? activeTime,
        double? responseTime)
    {
        DiskAlert? lastAlert = null;
        for (int index = 0; index < count; index++)
        {
            lastAlert = evaluator.Evaluate(CreateMetrics(startSeconds + index, activeTime, responseTime)) ?? lastAlert;
        }

        return lastAlert;
    }

    private static DiskMetrics CreateMetrics(
        int seconds,
        double? activeTime,
        double? responseTime,
        IReadOnlyDictionary<string, string>? unavailable = null) =>
        new(
            DateTimeOffset.UnixEpoch.AddSeconds(seconds),
            activeTime,
            0,
            0,
            responseTime,
            0,
            null,
            unavailable ?? new Dictionary<string, string>());
}
