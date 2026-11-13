using DiskMonitor.Core.Models;
using DiskMonitor.Windows.Performance;

using var cancellationSource = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

Console.CancelKeyPress += cancelHandler;

try
{
    Console.WriteLine("Reading PhysicalDisk(_Total) metrics; press Ctrl+C to stop.");

    var provider = new WindowsDiskMetricsProvider();
    int samples = 0;
    await foreach (DiskMetrics metrics in provider.ReadMetricsAsync(cancellationSource.Token))
    {
        Console.WriteLine(
            $"{metrics.TimestampUtc:O} | " +
            $"Active: {Format(metrics.ActiveTimePercent)}% | " +
            $"Read: {Format(metrics.ReadThroughputMBps)} MB/s | " +
            $"Write: {Format(metrics.WriteThroughputMBps)} MB/s | " +
            $"Response: {Format(metrics.AverageResponseTimeMilliseconds)} ms | " +
            $"Queue: {Format(metrics.CurrentDiskQueueLength)}");

        if (metrics.SystemVolumeSpace is DiskSpaceMetrics space)
        {
            Console.WriteLine(
                $"  System volume {space.VolumeName}: " +
                $"{space.AvailableFreeBytes:N0} B free of {space.CapacityBytes:N0} B");
        }

        foreach ((string metric, string reason) in metrics.UnavailableMetrics)
        {
            Console.Error.WriteLine($"  {metric} unavailable: {reason}");
        }

        if (++samples >= 5)
        {
            break;
        }
    }
}
catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
{
    Console.WriteLine("Collection cancelled.");
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}

static string Format(double? value) => value?.ToString("F2") ?? "unavailable";
