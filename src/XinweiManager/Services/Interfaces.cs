using XinweiManager.Models;

namespace XinweiManager.Services;

public interface ISystemMetricsService { SystemSnapshot Sample(); }
public interface ISensorService : IDisposable { IReadOnlyList<SensorReading> Sample(); }
public interface IProcessMetricsService
{
    ProcessSnapshot Sample();
    void Reset();
}
public interface IHardwareInventoryService { HardwareInventory Read(); }
public interface ICleanupService
{
    Task<CleanupScanResult> ScanAsync(CancellationToken token);
    Task<CleanupResult> CleanAsync(CleanupScanResult scan, CancellationToken token);
}
public interface ILargeFileService
{
    IReadOnlyList<(string Name, string Path)> GetDefaultLocations();
    bool IsSafeRoot(string path, out string reason);
    Task<LargeFileScanResult> ScanAsync(IEnumerable<string> roots, long minimumBytes,
        int minimumAgeDays, IProgress<LargeFileScanProgress>? progress, CancellationToken token);
    Task<LargeFileActionResult> MoveToRecycleBinAsync(IEnumerable<CleanupCandidate> candidates,
        CancellationToken token);
}
public interface IStartupService
{
    bool IsDeployed { get; }
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}
