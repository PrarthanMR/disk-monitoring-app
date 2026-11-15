using DiskMonitor.Core.Models;
using DiskMonitor.Windows.Performance;

namespace DiskMonitor.Tests.Startup;

public sealed class WindowsDiskMetricsProviderTests
{
    [Fact]
    public async Task ReadsActualPhysicalDiskActiveTimeCounter()
    {
        var provider = new WindowsDiskMetricsProvider();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using IAsyncEnumerator<DiskActiveTimeSample> enumerator =
            provider.ReadActiveTimeAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);

        DiskActiveTimeSample? validSample = null;
        string? lastUnavailableReason = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Assert.True(await enumerator.MoveNextAsync());
            DiskActiveTimeSample sample = enumerator.Current;
            Assert.Equal(TimeSpan.Zero, sample.TimestampUtc.Offset);
            Assert.True(sample.TimestampUtc > DateTimeOffset.UtcNow.AddSeconds(-15));

            if (sample.ActiveTimePercent is double activeTime)
            {
                Assert.True(double.IsFinite(activeTime) && activeTime is >= 0 and <= 100);
                validSample = sample;
                break;
            }

            lastUnavailableReason = sample.UnavailableReason;
            Assert.False(string.IsNullOrWhiteSpace(lastUnavailableReason));
        }

        Assert.True(
            validSample is not null,
            $"The real PhysicalDisk(_Total) counter did not return a valid sample: {lastUnavailableReason}");
    }

    [Fact]
    public async Task CancellationStopsTheRealCounterStream()
    {
        var provider = new WindowsDiskMetricsProvider();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<DiskActiveTimeSample> enumerator =
            provider.ReadActiveTimeAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await enumerator.MoveNextAsync());
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(0, 0)]
    [InlineData(40, 40)]
    public void AcceptsValidActiveTimeReadings(double activeTimePercent, double expectedActiveTimePercent)
    {
        Assert.Equal(
            expectedActiveTimePercent,
            WindowsDiskMetricsProvider.ValidateActiveTimePercent(activeTimePercent));
    }

    [Theory]
    [InlineData(100.005)]
    [InlineData(-0.005)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidActiveTimeReadings(double activeTimePercent)
    {
        Assert.Null(WindowsDiskMetricsProvider.ValidateActiveTimePercent(activeTimePercent));
    }
}
