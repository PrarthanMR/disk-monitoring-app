using DiskMonitor.Core.Models;
using DiskMonitor.Core.Services;

namespace DiskMonitor.Tests.Core;

public sealed class ReadinessEvaluatorTests
{
    [Fact]
    public void RequiresThirtySecondBaselineAndRecentStableWindowBeforeReady()
    {
        var evaluator = new ReadinessEvaluator();
        SystemReadiness? readiness = null;

        for (int seconds = 0; seconds <= 29; seconds++)
        {
            readiness = evaluator.Evaluate(CreateMetrics(seconds, 20, 5));
        }

        Assert.Equal(SystemReadinessState.Initializing, readiness?.State);

        readiness = evaluator.Evaluate(CreateMetrics(30, 20, 5));

        Assert.Equal(SystemReadinessState.Ready, readiness.State);
        Assert.Contains("baseline", readiness.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SustainedHighActivityAfterBaselineIsDegraded()
    {
        var evaluator = new ReadinessEvaluator();
        SystemReadiness? readiness = null;
        for (int seconds = 0; seconds <= 30; seconds++)
        {
            readiness = evaluator.Evaluate(CreateMetrics(seconds, 95, 10));
        }

        Assert.Equal(SystemReadinessState.Degraded, readiness?.State);
        Assert.False(string.IsNullOrWhiteSpace(readiness?.Explanation));
    }

    [Fact]
    public void MissingMetricsAfterBaselineRequiresWaiting()
    {
        var evaluator = new ReadinessEvaluator();
        SystemReadiness? readiness = null;
        for (int seconds = 0; seconds <= 29; seconds++)
        {
            readiness = evaluator.Evaluate(CreateMetrics(seconds, 20, 5));
        }

        readiness = evaluator.Evaluate(CreateMetrics(30, 20, 5));
        readiness = evaluator.Evaluate(CreateMetrics(
            31,
            null,
            5,
            new Dictionary<string, string> { ["Disk active time"] = "Counter is unavailable." }));

        Assert.Equal(SystemReadinessState.Wait, readiness.State);
        Assert.Contains("Counter is unavailable.", readiness.Explanation);
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
