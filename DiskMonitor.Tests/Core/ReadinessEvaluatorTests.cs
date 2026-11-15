using DiskMonitor.Core.Configuration;
using DiskMonitor.Core.Models;
using DiskMonitor.Core.Services;

namespace DiskMonitor.Tests.Core;

public sealed class ReadinessEvaluatorTests
{
    [Fact]
    public void NormalActivityBecomesReadyAfterFullStableDuration()
    {
        var evaluator = new ReadinessEvaluator();
        ReadinessUpdate? update = null;

        for (int seconds = 0; seconds < 60; seconds++)
        {
            update = evaluator.Evaluate(CreateSample(seconds, 10));
            Assert.Equal(ReadinessState.PleaseWait, update.State);
        }

        update = evaluator.Evaluate(CreateSample(60, 10));

        Assert.Equal(ReadinessState.Ready, update.State);
        Assert.Equal(10, update.ActiveTimePercent);
        Assert.Equal(TimeSpan.FromSeconds(60), update.StableElapsed);
        Assert.Contains("ready for normal use", update.Explanation);
    }

    [Fact]
    public void TemporarySpikeResetsStabilityTimer()
    {
        var evaluator = new ReadinessEvaluator();
        for (int seconds = 0; seconds <= 30; seconds++)
        {
            evaluator.Evaluate(CreateSample(seconds, 10));
        }

        ReadinessUpdate spike = evaluator.Evaluate(CreateSample(31, 55));
        Assert.Equal(ReadinessState.PleaseWait, spike.State);
        Assert.Equal(TimeSpan.Zero, spike.StableElapsed);

        for (int seconds = 32; seconds < 92; seconds++)
        {
            evaluator.Evaluate(CreateSample(seconds, 10));
        }

        ReadinessUpdate update = evaluator.Evaluate(CreateSample(92, 10));
        Assert.Equal(ReadinessState.Ready, update.State);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(85)]
    [InlineData(100)]
    public void ActivityAtOrAboveThresholdShowsPleaseWait(double activeTime)
    {
        var evaluator = new ReadinessEvaluator();

        ReadinessUpdate update = evaluator.Evaluate(CreateSample(0, activeTime));

        Assert.Equal(ReadinessState.PleaseWait, update.State);
        Assert.Equal(activeTime, update.ActiveTimePercent);
        Assert.Equal(TimeSpan.Zero, update.StableElapsed);
    }

    [Fact]
    public void ActivityRisingAfterReadyReturnsStatusToPleaseWait()
    {
        var evaluator = new ReadinessEvaluator();
        for (int seconds = 0; seconds <= 60; seconds++)
        {
            evaluator.Evaluate(CreateSample(seconds, 5));
        }

        ReadinessUpdate update = evaluator.Evaluate(CreateSample(61, 20));

        Assert.Equal(ReadinessState.PleaseWait, update.State);
        Assert.Equal(20, update.ActiveTimePercent);
        Assert.Equal(TimeSpan.Zero, update.StableElapsed);
    }

    [Fact]
    public void InvalidMetricNeverBecomesZeroAndResetsStability()
    {
        var evaluator = new ReadinessEvaluator();
        for (int seconds = 0; seconds <= 30; seconds++)
        {
            evaluator.Evaluate(CreateSample(seconds, 10));
        }

        ReadinessUpdate unavailable = evaluator.Evaluate(
            new DiskActiveTimeSample(
                DateTimeOffset.UnixEpoch.AddSeconds(31),
                null,
                "Counter is unavailable."));

        Assert.Equal(ReadinessState.PleaseWait, unavailable.State);
        Assert.Null(unavailable.ActiveTimePercent);
        Assert.Equal(TimeSpan.Zero, unavailable.StableElapsed);
        Assert.Contains("Counter is unavailable.", unavailable.Explanation);

        ReadinessUpdate resumed = evaluator.Evaluate(CreateSample(32, 10));
        Assert.Equal(ReadinessState.PleaseWait, resumed.State);
        Assert.Equal(TimeSpan.Zero, resumed.StableElapsed);
    }

    [Fact]
    public void FirstUnavailableSampleKeepsStatusInitializing()
    {
        var evaluator = new ReadinessEvaluator();

        ReadinessUpdate update = evaluator.Evaluate(
            new DiskActiveTimeSample(DateTimeOffset.UnixEpoch, null, "Counter unavailable."));

        Assert.Equal(ReadinessState.Initializing, update.State);
        Assert.Null(update.ActiveTimePercent);
    }

    [Fact]
    public void SamplingGapResetsStabilityWindow()
    {
        var evaluator = new ReadinessEvaluator();
        for (int seconds = 0; seconds <= 30; seconds++)
        {
            evaluator.Evaluate(CreateSample(seconds, 10));
        }

        ReadinessUpdate afterGap = evaluator.Evaluate(CreateSample(32, 10));

        Assert.Equal(ReadinessState.PleaseWait, afterGap.State);
        Assert.Equal(TimeSpan.Zero, afterGap.StableElapsed);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(100.1)]
    public void InvalidNumericMetricsAreUnavailable(double activeTime)
    {
        var evaluator = new ReadinessEvaluator();

        ReadinessUpdate update = evaluator.Evaluate(CreateSample(0, activeTime));

        Assert.Null(update.ActiveTimePercent);
        Assert.Equal(ReadinessState.Initializing, update.State);
    }

    [Fact]
    public void RejectsNonIncreasingTimestamps()
    {
        var evaluator = new ReadinessEvaluator();
        evaluator.Evaluate(CreateSample(0, 10));

        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(CreateSample(0, 10)));
    }

    [Fact]
    public void UsesConfiguredThresholdAndDuration()
    {
        var evaluator = new ReadinessEvaluator(
            new ReadinessOptions(ActiveTimeThresholdPercent: 15, StableDurationSeconds: 2));
        evaluator.Evaluate(CreateSample(0, 14.9));
        evaluator.Evaluate(CreateSample(1, 14.9));

        ReadinessUpdate update = evaluator.Evaluate(CreateSample(2, 14.9));

        Assert.Equal(ReadinessState.Ready, update.State);
    }

    private static DiskActiveTimeSample CreateSample(int seconds, double activeTime) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(seconds), activeTime, null);
}
