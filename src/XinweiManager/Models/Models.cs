namespace XinweiManager.Models;

public sealed record SystemSnapshot(double? CpuPercent, uint? MemoryPercent,
    ulong? TotalMemoryBytes, ulong? AvailableMemoryBytes, DateTimeOffset SampledAt);

public enum SensorKind { CpuTemperature, GpuTemperature, GpuLoad, Fan }

public sealed record SensorReading(SensorKind Kind, string Device, string Name,
    float Value, DateTimeOffset SampledAt);

public sealed record ProcessUsage(int Pid, string Name, double CpuPercent,
    long WorkingSetBytes);

public sealed record ProcessSnapshot(IReadOnlyList<ProcessUsage> ByCpu,
    IReadOnlyList<ProcessUsage> ByMemory, bool CpuReady, DateTimeOffset SampledAt);

public sealed record HardwareSection(string Title, IReadOnlyList<string> Lines);

public sealed record HardwareInventory(IReadOnlyList<HardwareSection> Sections, DateTimeOffset SampledAt);

public sealed record CleanupCandidate(string Root, string FullPath, string RelativePath,
    long Length, DateTime LastWriteUtc);

public sealed record CleanupScanResult(IReadOnlyList<CleanupCandidate> Candidates,
    int Skipped, IReadOnlyList<string> Warnings, DateTimeOffset ScannedAt)
{
    public long EstimatedBytes => Candidates.Sum(x => x.Length);
}

public sealed record CleanupResult(long FreedBytes, int Deleted, int Skipped,
    IReadOnlyList<string> Warnings);

public sealed record LargeFileScanResult(IReadOnlyList<CleanupCandidate> Candidates,
    int Skipped, IReadOnlyList<string> Warnings, DateTimeOffset ScannedAt);

public sealed record LargeFileActionResult(long MovedBytes, int Moved, int Skipped);
