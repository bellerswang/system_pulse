using XinweiManager.Models;

namespace XinweiManager.Services;

public sealed class CleanupService : ICleanupService
{
    private static readonly TimeSpan MinimumAge = TimeSpan.FromHours(48);
    private readonly string[] _roots;

    public CleanupService(IEnumerable<string> roots)
    {
        _roots = roots.Select(ValidateRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (_roots.Length == 0) throw new ArgumentException("No safe temporary folders were provided.");
    }

    public static CleanupService CreateDefault()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return new CleanupService([Path.Combine(local, "Temp"), Path.Combine(windows, "Temp")]);
    }

    public Task<CleanupScanResult> ScanAsync(CancellationToken token) => Task.Run(() =>
    {
        var candidates = new List<CleanupCandidate>();
        var warnings = new List<string>();
        var skipped = 0;
        var cutoff = DateTime.UtcNow - MinimumAge;
        foreach (var root in _roots)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(root)) { warnings.Add($"Temporary folder unavailable: {root}"); continue; }
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                { warnings.Add($"Temporary folder is a link and was skipped: {root}"); continue; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { warnings.Add($"Temporary folder unavailable: {root}"); continue; }
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                try
                {
                    foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var attrs = File.GetAttributes(path);
                            if ((attrs & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) { skipped++; continue; }
                            if ((attrs & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                            if ((attrs & FileAttributes.ReadOnly) != 0) { skipped++; continue; }
                            var info = new FileInfo(path);
                            if (info.LastWriteTimeUtc > cutoff) { skipped++; continue; }
                            candidates.Add(new CleanupCandidate(root, info.FullName,
                                Path.GetRelativePath(root, info.FullName), info.Length, info.LastWriteTimeUtc));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { skipped++; }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { skipped++; warnings.Add($"Some items could not be scanned in {root}."); }
            }
        }
        return new CleanupScanResult(candidates, skipped, warnings.Distinct().ToArray(), DateTimeOffset.Now);
    }, token);

    public Task<CleanupResult> CleanAsync(CleanupScanResult scan, CancellationToken token) => Task.Run(() =>
    {
        long freed = 0;
        var deleted = 0;
        var skipped = 0;
        var parents = new HashSet<(string Root, string Directory)>();
        foreach (var candidate in scan.Candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!_roots.Contains(candidate.Root, StringComparer.OrdinalIgnoreCase) ||
                    !IsSafeChild(candidate.Root, candidate.FullPath)) { skipped++; continue; }
                var attrs = File.GetAttributes(candidate.FullPath);
                if ((attrs & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.ReadOnly)) != 0)
                { skipped++; continue; }
                var info = new FileInfo(candidate.FullPath);
                if (info.Length != candidate.Length || info.LastWriteTimeUtc != candidate.LastWriteUtc ||
                    DateTime.UtcNow - info.LastWriteTimeUtc < MinimumAge) { skipped++; continue; }
                File.Delete(candidate.FullPath);
                freed += info.Length;
                deleted++;
                var parent = info.DirectoryName;
                while (parent is not null && IsSafeChild(candidate.Root, parent))
                { parents.Add((candidate.Root, parent)); parent = Path.GetDirectoryName(parent); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { skipped++; }
        }
        foreach (var parent in parents.OrderByDescending(x => x.Directory.Length))
        {
            try
            {
                if (IsSafeChild(parent.Root, parent.Directory) && Directory.Exists(parent.Directory) &&
                    (File.GetAttributes(parent.Directory) & FileAttributes.ReparsePoint) == 0)
                    Directory.Delete(parent.Directory, false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return new CleanupResult(freed, deleted, skipped,
            skipped > 0 ? ["Some files were busy, changed, or unavailable and were skipped."] : []);
    }, token);

    private static string ValidateRoot(string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!string.Equals(Path.GetFileName(full), "Temp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Cleanup roots must be dedicated Temp folders.", nameof(root));
        return full;
    }

    private static bool IsSafeChild(string root, string path)
    {
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            return false;
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(root, full);
        if (relative == "." || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) ||
            Path.IsPathRooted(relative)) return false;
        var current = Path.GetDirectoryName(full);
        while (current is not null && !string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(current) || (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return false;
            current = Path.GetDirectoryName(current);
        }
        return current is not null;
    }
}
