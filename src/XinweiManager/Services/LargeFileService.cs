using Microsoft.VisualBasic.FileIO;
using System.Runtime.InteropServices;
using XinweiManager.Models;

namespace XinweiManager.Services;

public sealed class LargeFileService : ILargeFileService
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".idea", ".cache", "cache", "caches", "temp", "tmp",
        "node_modules", "bin", "obj", "packages", ".venv", "venv", "__pycache__", "npm-cache",
        "nuget", "apps", "applications", "programs", "program files", "program files (x86)",
        "windowsapps", "games", "steamlibrary", "steam", "epic games", "riot games", "ea games",
        "ubisoft", "xboxgames"
    };

    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    public IReadOnlyList<(string Name, string Path)> GetDefaultLocations()
    {
        var locations = new List<(string Name, string Path)>
        {
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Downloads", GetDownloadsPath()),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            ("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic))
        };

        return locations
            .Where(x => !string.IsNullOrWhiteSpace(x.Path) && Directory.Exists(x.Path))
            .Select(x => (x.Name, Path: Normalize(x.Path)))
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Where(x => IsSafeRoot(x.Path, out _))
            .ToArray();
    }

    public bool IsSafeRoot(string path, out string reason)
    {
        reason = "";
        string full;
        try { full = Normalize(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { reason = "Choose a valid folder."; return false; }

        if (!Directory.Exists(full)) { reason = "The folder does not exist or is unavailable."; return false; }
        if (string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
        { reason = "Drive roots cannot be scanned."; return false; }
        if (ExcludedDirectoryNames.Contains(Path.GetFileName(full)))
        { reason = "This folder is reserved for software or temporary data."; return false; }
        if (OverlapsProtectedSystemLocation(full))
        { reason = "System, application, and app-data folders are excluded."; return false; }

        try
        {
            var attributes = File.GetAttributes(full);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0)
            { reason = "Hidden, system, and linked folders are excluded."; return false; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { reason = "The folder cannot be accessed."; return false; }

        return true;
    }

    public Task<LargeFileScanResult> ScanAsync(IEnumerable<string> roots, long minimumBytes,
        int minimumAgeDays, CancellationToken token) => Task.Run(() =>
    {
        if (minimumBytes < 1 || minimumAgeDays < 1)
            throw new ArgumentOutOfRangeException(nameof(minimumAgeDays));

        var candidates = new List<CleanupCandidate>();
        var warnings = new List<string>();
        var skipped = 0;
        var cutoff = DateTime.UtcNow.AddDays(-minimumAgeDays);

        foreach (var providedRoot in roots.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (!IsSafeRoot(providedRoot, out _))
            { warnings.Add($"A selected folder was unavailable or excluded: {providedRoot}"); continue; }

            var pending = new Stack<string>();
            pending.Push(providedRoot);
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                if (IsInsideApplicationFolder(directory)) continue;

                try
                {
                    foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var attributes = File.GetAttributes(path);
                            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0 ||
                                IsCloudPlaceholder(attributes))
                            { skipped++; continue; }

                            if ((attributes & FileAttributes.Directory) != 0)
                            {
                                if (ExcludedDirectoryNames.Contains(Path.GetFileName(path))) { skipped++; continue; }
                                pending.Push(path);
                                continue;
                            }

                            var info = new FileInfo(path);
                            if (info.Length < minimumBytes || info.LastWriteTimeUtc > cutoff) continue;
                            candidates.Add(new CleanupCandidate(providedRoot, info.FullName,
                                Path.GetRelativePath(providedRoot, info.FullName), info.Length, info.LastWriteTimeUtc));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                        { skipped++; }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { skipped++; warnings.Add("Some items in a selected folder could not be scanned."); }
            }
        }

        return new LargeFileScanResult(candidates.OrderByDescending(x => x.Length).ToArray(), skipped,
            warnings.Distinct().ToArray(), DateTimeOffset.Now);
    }, token);

    public Task<LargeFileActionResult> MoveToRecycleBinAsync(IEnumerable<CleanupCandidate> candidates,
        CancellationToken token) => Task.Run(() =>
    {
        long movedBytes = 0;
        var moved = 0;
        var skipped = 0;

        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var root = Normalize(candidate.Root);
                var fullPath = Path.GetFullPath(candidate.FullPath);
                if (!IsSafeRoot(root, out _) || !IsSafeChild(root, fullPath)) { skipped++; continue; }

                var attributes = File.GetAttributes(fullPath);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0 ||
                    IsCloudPlaceholder(attributes))
                { skipped++; continue; }

                var info = new FileInfo(fullPath);
                if (info.Length != candidate.Length || info.LastWriteTimeUtc != candidate.LastWriteUtc)
                { skipped++; continue; }

                FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                movedBytes += candidate.Length;
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { skipped++; }
        }

        return new LargeFileActionResult(movedBytes, moved, skipped);
    }, token);

    private bool IsSafeChild(string root, string path)
    {
        if (!IsSafeRoot(root, out _)) return false;
        var relative = Path.GetRelativePath(root, path);
        if (relative is "." or ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative)) return false;

        var current = Path.GetDirectoryName(path);
        while (current is not null && !string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
        {
            if (IsInsideApplicationFolder(current) || ExcludedDirectoryNames.Contains(Path.GetFileName(current))) return false;
            try
            {
                if ((File.GetAttributes(current) & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0)
                    return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
            current = Path.GetDirectoryName(current);
        }
        return current is not null;
    }

    private static bool OverlapsProtectedSystemLocation(string path)
    {
        foreach (var protectedPath in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Normalize))
        {
            if (IsSameOrChild(path, protectedPath) || IsSameOrChild(protectedPath, path)) return true;
        }

        return false;
    }

    private static bool IsInsideApplicationFolder(string path) =>
        IsSameOrChild(Normalize(path), Normalize(AppContext.BaseDirectory));

    private static bool IsSameOrChild(string path, string parent) =>
        string.Equals(path, parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsCloudPlaceholder(FileAttributes attributes) =>
        (attributes & FileAttributes.Offline) != 0 ||
        (attributes & (FileAttributes)0x00040000) != 0 ||
        (attributes & (FileAttributes)0x00400000) != 0;

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string GetDownloadsPath()
    {
        IntPtr result = IntPtr.Zero;
        try
        {
            var id = DownloadsFolderId;
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out result) == 0)
                return Marshal.PtrToStringUni(result) ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
        catch (DllNotFoundException) { }
        finally { if (result != IntPtr.Zero) Marshal.FreeCoTaskMem(result); }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid knownFolderId, uint flags, IntPtr token, out IntPtr path);
}
