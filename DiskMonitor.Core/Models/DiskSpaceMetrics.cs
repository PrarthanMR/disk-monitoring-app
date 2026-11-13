namespace DiskMonitor.Core.Models;

/// <summary>
/// Informational capacity and available-space details for a disk volume.
/// </summary>
/// <param name="VolumeName">The volume root, such as <c>C:\</c>.</param>
/// <param name="CapacityBytes">The total capacity of the volume in bytes.</param>
/// <param name="AvailableFreeBytes">The space available to the current user in bytes.</param>
public sealed record DiskSpaceMetrics(
    string VolumeName,
    long CapacityBytes,
    long AvailableFreeBytes);
