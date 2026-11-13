using DiskMonitor.Core.Models;

namespace DiskMonitor.Core.Interfaces;

/// <summary>
/// Provides a cancellable asynchronous stream of disk metric snapshots.
/// </summary>
public interface IDiskMetricsProvider
{
    /// <summary>
    /// Reads disk metrics at one-second intervals until the operation is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Token used to stop collection and release resources.</param>
    /// <returns>A stream of timestamped disk metric snapshots.</returns>
    IAsyncEnumerable<DiskMetrics> ReadMetricsAsync(CancellationToken cancellationToken = default);
}
