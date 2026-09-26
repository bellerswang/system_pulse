using XinweiManager.Services;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}

Check(SystemMetricsService.CalculateCpuPercent(100, 500, 120, 600) == 80,
    "CPU percentage uses idle and total deltas");
Check(SystemMetricsService.CalculateCpuPercent(100, 500, 120, 500) is null,
    "First or zero-delta CPU sample is unavailable");
Check(!SensorService.IsUsable(float.NaN) && !SensorService.IsUsable(float.PositiveInfinity)
    && SensorService.IsUsable(0), "Invalid sensors are rejected and a stopped fan can read zero");
Check(!SensorService.IsUsableTemperature(0) && SensorService.IsUsableTemperature(46),
    "Zero temperature is treated as a missing reading");
Check(ProcessMetricsService.CalculateCpuPercent(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4),
    TimeSpan.FromSeconds(2), 4) == 25, "Per-process CPU is normalised across logical processors");

var testParent = Path.Combine(Path.GetTempPath(), "XinweiManager.Tests", Guid.NewGuid().ToString("N"));
var root = Path.Combine(testParent, "Temp");
Directory.CreateDirectory(root);
try
{
    var old = Path.Combine(root, "old.tmp");
    var recent = Path.Combine(root, "recent.tmp");
    var changed = Path.Combine(root, "changed.tmp");
    File.WriteAllText(old, "old data");
    File.WriteAllText(recent, "recent data");
    File.WriteAllText(changed, "first version");
    File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-4));
    File.SetLastWriteTimeUtc(changed, DateTime.UtcNow.AddDays(-4));

    var service = new CleanupService([root]);
    var scan = await service.ScanAsync(CancellationToken.None);
    Check(scan.Candidates.Count == 2 && scan.EstimatedBytes > 0,
        "Scan includes only files older than 48 hours");
    File.AppendAllText(changed, " changed after scan");
    var clean = await service.CleanAsync(scan, CancellationToken.None);
    Check(clean.Deleted == 1 && clean.Skipped == 1 && !File.Exists(old)
        && File.Exists(recent) && File.Exists(changed),
        "Cleanup skips changed and recent files");
    Check(clean.FreedBytes == "old data".Length,
        "Freed space counts only successfully deleted files");

    var locked = Path.Combine(root, "locked.tmp");
    File.WriteAllText(locked, "locked data");
    File.SetLastWriteTimeUtc(locked, DateTime.UtcNow.AddDays(-4));
    var lockedScan = await service.ScanAsync(CancellationToken.None);
    using (var lockHandle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        var lockedResult = await service.CleanAsync(lockedScan, CancellationToken.None);
        Check(lockedResult.Skipped >= 1 && File.Exists(locked),
            "Busy files are skipped without ending cleanup");
    }

    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try { await service.ScanAsync(cancelled.Token); throw new Exception("Cancelled scan ran"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS scan cancellation"); }

    try
    {
        var outside = Path.Combine(testParent, "outside.tmp");
        File.WriteAllText(outside, "outside");
        var link = Path.Combine(root, "link.tmp");
        File.CreateSymbolicLink(link, outside);
        var linkScan = await service.ScanAsync(CancellationToken.None);
        Check(linkScan.Candidates.All(x => x.FullPath != link), "Symbolic links are skipped");
    }
    catch (UnauthorizedAccessException) { Console.WriteLine("SKIP symbolic link creation requires privilege"); }
    catch (IOException) { Console.WriteLine("SKIP symbolic link creation unsupported here"); }
}
finally { Directory.Delete(testParent, true); }

Console.WriteLine("All checks passed.");
