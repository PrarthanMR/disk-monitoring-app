using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security;
using DiskMonitor.Core.Interfaces;
using DiskMonitor.Core.Models;

namespace DiskMonitor.Windows.Performance;

/// <summary>
/// Collects total physical-disk performance counters and system-volume space on Windows.
/// </summary>
public sealed class WindowsDiskMetricsProvider : IDiskMetricsProvider
{
    private static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(1);

    private static readonly CounterDefinition[] CounterDefinitions =
    [
        new("Disk active time", "% Idle Time"),
        new("Disk read throughput", "Disk Read Bytes/sec"),
        new("Disk write throughput", "Disk Write Bytes/sec"),
        new("Average disk response time", "Avg. Disk sec/Transfer"),
        new("Current disk queue length", "Current Disk Queue Length")
    ];

    /// <inheritdoc />
    public async IAsyncEnumerable<DiskMetrics> ReadMetricsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CounterReader[] counters = await Task.Run(CreateCounterReaders).ConfigureAwait(false);
        using var timer = new PeriodicTimer(SamplingInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return await Task.Run(() => CaptureMetrics(counters)).ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (CounterReader counter in counters)
            {
                counter.Dispose();
            }
        }
    }

    private static CounterReader[] CreateCounterReaders() =>
        CounterDefinitions.Select(CounterReader.Create).ToArray();

    private static DiskMetrics CaptureMetrics(IReadOnlyList<CounterReader> counters)
    {
        var unavailable = new Dictionary<string, string>();

        double? ReadCounter(int index, Func<double, double>? convert = null)
        {
            double? value = counters[index].ReadNext(out string? error);
            if (error is not null)
            {
                unavailable[counters[index].MetricName] = error;
            }

            return value is double rawValue ? convert?.Invoke(rawValue) ?? rawValue : null;
        }

        double? idleTime = ReadCounter(0);
        double? activeTime = idleTime is double idle ? 100 - idle : null;
        double? readThroughput = ReadCounter(1, bytesPerSecond => bytesPerSecond / 1_000_000);
        double? writeThroughput = ReadCounter(2, bytesPerSecond => bytesPerSecond / 1_000_000);
        double? responseTime = ReadCounter(3, seconds => seconds * 1_000);
        double? queueLength = ReadCounter(4);
        DiskSpaceMetrics? systemVolumeSpace = ReadSystemVolumeSpace(unavailable);

        return new DiskMetrics(
            DateTimeOffset.UtcNow,
            activeTime,
            readThroughput,
            writeThroughput,
            responseTime,
            queueLength,
            systemVolumeSpace,
            unavailable);
    }

    private static DiskSpaceMetrics? ReadSystemVolumeSpace(IDictionary<string, string> unavailable)
    {
        string? systemVolume = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrEmpty(systemVolume))
        {
            unavailable["System volume space"] = "The system volume could not be identified.";
            return null;
        }

        try
        {
            var drive = new DriveInfo(systemVolume);
            if (!drive.IsReady)
            {
                unavailable["System volume space"] = $"Volume {systemVolume} is not ready.";
                return null;
            }

            return new DiskSpaceMetrics(systemVolume, drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception exception) when (IsExpectedCollectionException(exception))
        {
            unavailable["System volume space"] = exception.Message;
            return null;
        }
    }

    private static bool IsExpectedCollectionException(Exception exception) =>
        exception is ArgumentException
            or IOException
            or InvalidOperationException
            or NotSupportedException
            or SecurityException
            or UnauthorizedAccessException
            or Win32Exception;

    private sealed record CounterDefinition(string MetricName, string CounterName);

    private sealed class CounterReader : IDisposable
    {
        private readonly PerformanceCounter? _counter;
        private readonly string? _initializationError;

        private CounterReader(
            string metricName,
            PerformanceCounter? counter,
            string? initializationError)
        {
            MetricName = metricName;
            _counter = counter;
            _initializationError = initializationError;
        }

        public string MetricName { get; }

        public static CounterReader Create(CounterDefinition definition)
        {
            PerformanceCounter? counter = null;
            try
            {
                counter = new PerformanceCounter(
                    "PhysicalDisk",
                    definition.CounterName,
                    "_Total",
                    readOnly: true);
                counter.NextValue();

                return new CounterReader(definition.MetricName, counter, null);
            }
            catch (Exception exception) when (IsExpectedCollectionException(exception))
            {
                counter?.Dispose();
                return new CounterReader(definition.MetricName, null, exception.Message);
            }
        }

        public double? ReadNext(out string? error)
        {
            if (_counter is null)
            {
                error = _initializationError;
                return null;
            }

            try
            {
                float value = _counter.NextValue();
                if (!float.IsFinite(value))
                {
                    error = "The performance counter returned a non-finite value.";
                    return null;
                }

                error = null;
                return value;
            }
            catch (Exception exception) when (IsExpectedCollectionException(exception))
            {
                error = exception.Message;
                return null;
            }
        }

        public void Dispose() => _counter?.Dispose();
    }
}
