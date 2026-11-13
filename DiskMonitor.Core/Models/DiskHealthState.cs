namespace DiskMonitor.Core.Models;

/// <summary>The current disk-health evaluation state.</summary>
public enum DiskHealthState
{
    /// <summary>There is not yet enough rolling data to evaluate health.</summary>
    InsufficientData,

    /// <summary>A required metric is unavailable or invalid.</summary>
    Unavailable,

    /// <summary>No sustained performance concern has been detected.</summary>
    Normal,

    /// <summary>Disk activity has been elevated for the configured warning duration.</summary>
    Warning,

    /// <summary>Sustained critical latency or combined activity and latency was detected.</summary>
    Critical,

    /// <summary>Recovery conditions have persisted for the configured duration.</summary>
    Recovery
}
