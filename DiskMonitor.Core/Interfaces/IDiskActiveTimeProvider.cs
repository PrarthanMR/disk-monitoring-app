using DiskMonitor.Core.Models;

namespace DiskMonitor.Core.Interfaces;

/// <summary>Provides timestamped physical-disk active-time samples.</summary>
public interface IDiskActiveTimeProvider
{
    /// <summary>Reads aggregate disk active-time samples until cancellation or provider completion.</summary>
    IAsyncEnumerable<DiskActiveTimeSample> ReadActiveTimeAsync(
        CancellationToken cancellationToken = default);
}
