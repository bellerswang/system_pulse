namespace XinweiManager.Services;

public static class LocalLog
{
    private static readonly object Gate = new();
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SystemPulse", "logs");

    public static void Error(string operation, Exception exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                foreach (var file in Directory.EnumerateFiles(Folder, "*.log"))
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7)) File.Delete(file);
                File.AppendAllText(Path.Combine(Folder, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".log"),
                    $"{DateTimeOffset.UtcNow:O} {operation}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}");
            }
        }
        catch { /* Logging must never crash the app. */ }
    }
}
