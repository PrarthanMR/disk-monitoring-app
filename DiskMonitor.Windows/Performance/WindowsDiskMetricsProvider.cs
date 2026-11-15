using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security;
using DiskMonitor.Core.Interfaces;
using DiskMonitor.Core.Models;

namespace DiskMonitor.Windows.Performance;

public sealed class WindowsDiskMetricsProvider : IDiskActiveTimeProvider
{
    public async IAsyncEnumerable<DiskActiveTimeSample> ReadActiveTimeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PerformanceCounter? counter = null;
        string? initializationError = null;
        try
        {
            counter = CreateCounter(out initializationError);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return ReadActiveTime(counter, initializationError);
            }
        }
        finally
        {
            counter?.Dispose();
        }
    }

    internal static double? ValidateActiveTimePercent(double activeTimePercent)
    {
        if (!double.IsFinite(activeTimePercent)
            || activeTimePercent is < 0 or > 100)
        {
            return null;
        }

        return activeTimePercent;
    }

    private static PerformanceCounter? CreateCounter(out string? error)
    {
        PerformanceCounter? counter = null;
        try
        {
            counter = new PerformanceCounter(
                "PhysicalDisk",
                "% Disk Time",
                "_Total",
                readOnly: true);
            counter.NextValue();
            error = null;
            return counter;
        }
        catch (Exception exception) when (IsExpectedCounterException(exception))
        {
            counter?.Dispose();
            error = exception.Message;
            return null;
        }
    }

    private static DiskActiveTimeSample ReadActiveTime(
        PerformanceCounter? counter,
        string? initializationError)
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        if (counter is null)
        {
            return new DiskActiveTimeSample(
                timestamp,
                null,
                initializationError ?? "The PhysicalDisk % Disk Time counter could not be opened.");
        }

        try
        {
            float activeTimeReading = counter.NextValue();
            double? activeTime = ValidateActiveTimePercent(activeTimeReading);
            return activeTime is double value
                ? new DiskActiveTimeSample(timestamp, value, null)
                : new DiskActiveTimeSample(
                    timestamp,
                    null,
                    $"The PhysicalDisk % Disk Time counter returned an invalid value ({activeTimeReading:F3}%).");
        }
        catch (Exception exception) when (IsExpectedCounterException(exception))
        {
            return new DiskActiveTimeSample(timestamp, null, exception.Message);
        }
    }

    private static bool IsExpectedCounterException(Exception exception) =>
        exception is ArgumentException
            or InvalidOperationException
            or Win32Exception
            or IOException
            or SecurityException
            or UnauthorizedAccessException
            or PlatformNotSupportedException;
}
