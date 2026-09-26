using Microsoft.Win32;

namespace XinweiManager.Services;

public sealed class StartupService : IStartupService
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "SystemPulse";
    private readonly string _expectedExe = Path.Combine(AppContext.BaseDirectory, "SystemPulse.exe");

    public bool IsDeployed
    {
        get
        {
            var appFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            var projectRoot = Directory.GetParent(appFolder)?.FullName;
            var localPrototype = string.Equals(Path.GetFileName(appFolder), "app", StringComparison.OrdinalIgnoreCase)
                && projectRoot is not null
                && File.Exists(Path.Combine(projectRoot, "src", "XinweiManager", "XinweiManager.csproj"));
            var installed = File.Exists(Path.Combine(appFolder, "unins000.exe"));
            return (localPrototype || installed)
                && string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""),
                    Path.GetFullPath(_expectedExe), StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return string.Equals(key?.GetValue(ValueName) as string,
                    $"\"{_expectedExe}\" --background", StringComparison.OrdinalIgnoreCase);
            }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && !IsDeployed)
            throw new InvalidOperationException("Publish the local prototype before enabling launch at sign-in.");
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true)
            ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
        if (enabled) key.SetValue(ValueName, $"\"{_expectedExe}\" --background", RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
}
