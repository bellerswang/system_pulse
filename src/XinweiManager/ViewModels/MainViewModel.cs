using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using XinweiManager.Models;
using XinweiManager.Services;

namespace XinweiManager.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");
    private readonly ISystemMetricsService _metrics;
    private readonly ISensorService _sensors;
    private readonly IProcessMetricsService _processes;
    private readonly IHardwareInventoryService _inventory;
    private readonly ICleanupService _cleanup;
    private readonly ILargeFileService _largeFiles;
    private readonly IStartupService _startup;
    private readonly Dictionary<int, List<double>> _cpuTrends = [];
    private readonly Dictionary<int, List<double>> _memoryTrends = [];
    private static readonly Brush CpuAccent = MakeAccent(0, 229, 255);
    private static readonly Brush MemoryAccent = MakeAccent(255, 74, 174);
    private readonly CancellationTokenSource _life = new();
    private CancellationTokenSource? _operation;
    private CleanupScanResult? _scan;
    private bool _busy, _windowVisible, _startupEnabled, _cpuExpanded, _memoryExpanded, _isLargeFilesMode, _isLargeFileScanning;
    private string _cpuUsage = "Collecting data…", _memoryUsage = "Collecting data…";
    private string _memoryDetail = "", _cpuTemp = "Collecting data…", _gpuTemp = "Collecting data…";
    private string _gpuUsage = "Collecting data…";
    private string _fansStatus = "Collecting data…", _lastUpdated = "—";
    private string _cleanupStatus = "Scan temporary files to see what can be removed.";
    private string _hardwareStatus = "Open this page to load your hardware details.";
    private string _estimated = "0 B", _cleanupResult = "";
    private string _cpuProcessStatus = "Select to inspect the busiest processes.";
    private string _memoryProcessStatus = "Select to inspect memory use by process.";
    private string _largeFilesStatus = "Choose personal folders, then search for large files.";
    private string _largeFilesActionResult = "";
    private string _largeFilesProgressText = "Ready to search.";
    private int _largeFileAgeDays = 180;
    private int _largeFileMinimumMegabytes = 500;
    private double _cpuLoad, _memoryLoad, _gpuLoad;
    private ulong _totalMemoryBytes;

    public MainViewModel(ISystemMetricsService metrics, ISensorService sensors,
        IProcessMetricsService processes,
        IHardwareInventoryService inventory, ICleanupService cleanup, ILargeFileService largeFiles,
        IStartupService startup)
    {
        _metrics = metrics; _sensors = sensors; _processes = processes;
        _inventory = inventory; _cleanup = cleanup; _largeFiles = largeFiles; _startup = startup;
        _startupEnabled = startup.IsEnabled;
        foreach (var location in _largeFiles.GetDefaultLocations())
            LargeFileLocations.Add(new ScanLocationOption(location.Name, location.Path, true));
        ScanCommand = new RelayCommand(() => _ = ScanAsync(), () => !Busy);
        CleanCommand = new RelayCommand(() => _ = CleanAsync(), () => !Busy && _scan?.Candidates.Count > 0);
        CancelCommand = new RelayCommand(() => _operation?.Cancel(), () => Busy);
        ShowTemporaryFilesCommand = new RelayCommand(() => IsLargeFilesMode = false);
        ShowLargeFilesCommand = new RelayCommand(() => IsLargeFilesMode = true);
        SearchLargeFilesCommand = new RelayCommand(() => _ = SearchLargeFilesAsync(), () => !Busy);
        AddLargeFolderCommand = new RelayCommand(AddLargeFolder);
        SelectAllLargeFilesCommand = new RelayCommand(() => SetLargeFileSelection(true), () => !Busy && LargeFileItems.Count > 0);
        ClearLargeFileSelectionCommand = new RelayCommand(() => SetLargeFileSelection(false), () => !Busy && LargeFileItems.Count > 0);
        MoveLargeFilesCommand = new RelayCommand(() => _ = MoveLargeFilesAsync(), () => !Busy && LargeFileItems.Any(x => x.IsSelected));
        RefreshHardwareCommand = new RelayCommand(() => _ = RefreshHardwareAsync());
        ToggleStartupCommand = new RelayCommand(ToggleStartup);
        ToggleCpuCommand = new RelayCommand(ToggleCpu);
        ToggleMemoryCommand = new RelayCommand(ToggleMemory);
        _ = MetricsLoopAsync();
        _ = SensorLoopAsync();
        _ = ProcessLoopAsync();
    }

    public Func<CleanupScanResult, bool>? ConfirmClean { get; set; }
    public RelayCommand ScanCommand { get; }
    public RelayCommand CleanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ShowTemporaryFilesCommand { get; }
    public RelayCommand ShowLargeFilesCommand { get; }
    public RelayCommand SearchLargeFilesCommand { get; }
    public RelayCommand AddLargeFolderCommand { get; }
    public RelayCommand SelectAllLargeFilesCommand { get; }
    public RelayCommand ClearLargeFileSelectionCommand { get; }
    public RelayCommand MoveLargeFilesCommand { get; }
    public RelayCommand RefreshHardwareCommand { get; }
    public RelayCommand ToggleStartupCommand { get; }
    public RelayCommand ToggleCpuCommand { get; }
    public RelayCommand ToggleMemoryCommand { get; }
    public ObservableCollection<CleanupCandidate> Candidates { get; } = [];
    public ObservableCollection<SensorReading> CpuSensors { get; } = [];
    public ObservableCollection<SensorReading> GpuSensors { get; } = [];
    public ObservableCollection<SensorReading> Fans { get; } = [];
    public ObservableCollection<HardwareSection> Hardware { get; } = [];
    public ObservableCollection<ProcessRow> CpuProcesses { get; } = [];
    public ObservableCollection<ProcessRow> MemoryProcesses { get; } = [];
    public ObservableCollection<ScanLocationOption> LargeFileLocations { get; } = [];
    public ObservableCollection<LargeFileItem> LargeFileItems { get; } = [];
    public IReadOnlyList<FilterOption> LargeFileSizeOptions { get; } =
        [new("100 MiB", 100), new("500 MiB", 500), new("1 GiB", 1024)];
    public IReadOnlyList<FilterOption> LargeFileAgeOptions { get; } =
        [new("90 days", 90), new("180 days", 180), new("365 days", 365)];
    public Func<string?>? PickLargeFolder { get; set; }

    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) CommandManager.InvalidateRequerySuggested(); } }
    public bool WindowVisible
    {
        get => _windowVisible;
        set { if (Set(ref _windowVisible, value) && !value) _processes.Reset(); }
    }
    public bool IsLargeFilesMode
    {
        get => _isLargeFilesMode;
        set { if (Set(ref _isLargeFilesMode, value)) Notify(nameof(IsTemporaryFilesMode)); }
    }
    public bool IsTemporaryFilesMode => !IsLargeFilesMode;
    public int LargeFileMinimumMegabytes { get => _largeFileMinimumMegabytes; set => Set(ref _largeFileMinimumMegabytes, value); }
    public int LargeFileAgeDays { get => _largeFileAgeDays; set => Set(ref _largeFileAgeDays, value); }
    public string LargeFilesStatus { get => _largeFilesStatus; private set => Set(ref _largeFilesStatus, value); }
    public string LargeFilesActionResult { get => _largeFilesActionResult; private set => Set(ref _largeFilesActionResult, value); }
    public bool IsLargeFileScanning { get => _isLargeFileScanning; private set => Set(ref _isLargeFileScanning, value); }
    public string LargeFilesProgressText { get => _largeFilesProgressText; private set => Set(ref _largeFilesProgressText, value); }
    public string LargeFilesSelectedSummary =>
        $"{LargeFileItems.Count(x => x.IsSelected)} selected  ·  {FormatBytes(LargeFileItems.Where(x => x.IsSelected).Sum(x => x.Candidate.Length))}";
    public bool CpuExpanded { get => _cpuExpanded; private set { if (Set(ref _cpuExpanded, value)) Notify(nameof(CpuToggleLabel)); } }
    public bool MemoryExpanded { get => _memoryExpanded; private set { if (Set(ref _memoryExpanded, value)) Notify(nameof(MemoryToggleLabel)); } }
    public string CpuToggleLabel => CpuExpanded ? "HIDE PROCESSES  −" : "TOP PROCESSES  +";
    public string MemoryToggleLabel => MemoryExpanded ? "HIDE PROCESSES  −" : "TOP PROCESSES  +";
    public string CpuProcessStatus { get => _cpuProcessStatus; private set => Set(ref _cpuProcessStatus, value); }
    public string MemoryProcessStatus { get => _memoryProcessStatus; private set => Set(ref _memoryProcessStatus, value); }
    public bool StartupEnabled { get => _startupEnabled; private set => Set(ref _startupEnabled, value); }
    public bool StartupAvailable => _startup.IsDeployed;
    public string StartupLabel => StartupEnabled ? "Launch at sign-in: On" : "Launch at sign-in: Off";
    public string CpuUsage
    {
        get => _cpuUsage;
        private set { if (Set(ref _cpuUsage, value)) Notify(nameof(FloatingCpuUsage)); }
    }
    public double CpuLoad { get => _cpuLoad; private set => Set(ref _cpuLoad, value); }
    public string MemoryUsage
    {
        get => _memoryUsage;
        private set { if (Set(ref _memoryUsage, value)) Notify(nameof(FloatingMemoryUsage)); }
    }
    public double MemoryLoad { get => _memoryLoad; private set => Set(ref _memoryLoad, value); }
    public string MemoryDetail { get => _memoryDetail; private set => Set(ref _memoryDetail, value); }
    public string CpuTemperature
    {
        get => _cpuTemp;
        private set { if (Set(ref _cpuTemp, value)) Notify(nameof(FloatingCpuTemperature)); }
    }
    public string GpuTemperature
    {
        get => _gpuTemp;
        private set { if (Set(ref _gpuTemp, value)) Notify(nameof(FloatingGpuTemperature)); }
    }
    public string GpuUsage
    {
        get => _gpuUsage;
        private set { if (Set(ref _gpuUsage, value)) Notify(nameof(FloatingGpuUsage)); }
    }
    public double GpuLoad { get => _gpuLoad; private set => Set(ref _gpuLoad, value); }
    public string FloatingCpuTemperature => CompactReading(CpuTemperature, " °C", "°C");
    public string FloatingGpuTemperature => CompactReading(GpuTemperature, " °C", "°C");
    public string FloatingCpuUsage => CompactReading(CpuUsage, "%", "%");
    public string FloatingGpuUsage => CompactReading(GpuUsage, "%", "%");
    public string FloatingMemoryUsage => CompactReading(MemoryUsage, "%", "%");
    public string FansStatus { get => _fansStatus; private set => Set(ref _fansStatus, value); }
    public string LastUpdated { get => _lastUpdated; private set => Set(ref _lastUpdated, value); }
    public string CleanupStatus { get => _cleanupStatus; private set => Set(ref _cleanupStatus, value); }
    public string Estimated { get => _estimated; private set => Set(ref _estimated, value); }
    public string CleanupResult { get => _cleanupResult; private set => Set(ref _cleanupResult, value); }
    public string HardwareStatus { get => _hardwareStatus; private set => Set(ref _hardwareStatus, value); }
    public static string Unavailable => "Not available";
    public Func<int, long, bool>? ConfirmLargeFiles { get; set; }

    private void ToggleCpu()
    {
        CpuExpanded = !CpuExpanded;
        CpuProcessStatus = CpuExpanded ? "Sampling process CPU use…" : "Select to inspect the busiest processes.";
        if (!CpuExpanded) { CpuProcesses.Clear(); _cpuTrends.Clear(); }
        if (!CpuExpanded && !MemoryExpanded) _processes.Reset();
    }

    private void ToggleMemory()
    {
        MemoryExpanded = !MemoryExpanded;
        MemoryProcessStatus = MemoryExpanded ? "Loading process memory use…" : "Select to inspect memory use by process.";
        if (!MemoryExpanded) { MemoryProcesses.Clear(); _memoryTrends.Clear(); }
        if (!CpuExpanded && !MemoryExpanded) _processes.Reset();
    }

    public async Task RefreshHardwareAsync()
    {
        HardwareStatus = "Loading hardware details…";
        try
        {
            var result = await Task.Run(_inventory.Read, _life.Token);
            Hardware.Clear();
            foreach (var section in result.Sections) Hardware.Add(section);
            HardwareStatus = $"Last updated {result.SampledAt.ToString("dd MMM yyyy HH:mm:ss", English)}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { HardwareStatus = "Hardware details are unavailable."; LocalLog.Error("hardware inventory", ex); }
    }

    private async Task MetricsLoopAsync()
    {
        while (!_life.IsCancellationRequested)
        {
            try
            {
                var value = await Task.Run(_metrics.Sample, _life.Token);
                CpuUsage = value.CpuPercent is double cpu
                    ? double.IsNaN(cpu) ? "Unavailable" : $"{Math.Round(cpu, MidpointRounding.AwayFromZero):0}%"
                    : "Collecting data…";
                CpuLoad = value.CpuPercent is double load && !double.IsNaN(load) ? load : 0;
                MemoryUsage = value.MemoryPercent is uint memory ? $"{memory}%" : "Unavailable";
                MemoryLoad = value.MemoryPercent ?? 0;
                MemoryDetail = value.TotalMemoryBytes is ulong total && value.AvailableMemoryBytes is ulong available
                    ? $"{FormatBytes(total - available)} used of {FormatBytes(total)}" : "";
                _totalMemoryBytes = value.TotalMemoryBytes ?? 0;
                LastUpdated = value.SampledAt.ToString("dd MMM yyyy HH:mm:ss", English);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { CpuUsage = "Unavailable"; MemoryUsage = "Unavailable"; LocalLog.Error("system metrics", ex); }
            try { await Task.Delay(WindowVisible ? 2000 : 15000, _life.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SensorLoopAsync()
    {
        while (!_life.IsCancellationRequested)
        {
            try
            {
                var readings = await Task.Run(_sensors.Sample, _life.Token);
                Replace(CpuSensors, readings.Where(x => x.Kind == SensorKind.CpuTemperature));
                Replace(GpuSensors, readings.Where(x => x.Kind == SensorKind.GpuTemperature));
                Replace(Fans, readings.Where(x => x.Kind == SensorKind.Fan));
                CpuTemperature = Primary(CpuSensors, ["package", "tdie", "core average"]);
                GpuTemperature = Primary(GpuSensors, ["core"]);
                var gpuLoad = PrimaryGpuLoad(readings.Where(x => x.Kind == SensorKind.GpuLoad));
                GpuLoad = gpuLoad?.Value ?? 0;
                GpuUsage = gpuLoad is null ? Unavailable
                    : $"{Math.Round(gpuLoad.Value, MidpointRounding.AwayFromZero):0}%";
                FansStatus = Fans.Count == 0 ? Unavailable : $"{Fans.Count} sensor(s)";
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                CpuSensors.Clear(); GpuSensors.Clear(); Fans.Clear();
                CpuTemperature = Unavailable; GpuTemperature = Unavailable; GpuUsage = Unavailable; GpuLoad = 0; FansStatus = Unavailable;
                LocalLog.Error("hardware sensors", ex);
            }
            try { await Task.Delay(WindowVisible ? 5000 : 15000, _life.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessLoopAsync()
    {
        while (!_life.IsCancellationRequested)
        {
            var active = WindowVisible && (CpuExpanded || MemoryExpanded);
            if (active)
            {
                try
                {
                    var snapshot = await Task.Run(_processes.Sample, _life.Token);
                    if (CpuExpanded)
                    {
                        if (snapshot.CpuReady)
                        {
                            ReplaceProcesses(CpuProcesses, snapshot.ByCpu, true, _totalMemoryBytes);
                            CpuProcessStatus = "Current CPU share · bars compare to the top process";
                        }
                        else CpuProcessStatus = "Sampling process CPU use…";
                    }
                    if (MemoryExpanded)
                    {
                        ReplaceProcesses(MemoryProcesses, snapshot.ByMemory, false, _totalMemoryBytes);
                        MemoryProcessStatus = "Working set · bars compare to the top process";
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    CpuProcessStatus = "Process list unavailable.";
                    MemoryProcessStatus = "Process list unavailable.";
                    LocalLog.Error("process metrics", ex);
                }
            }
            try { await Task.Delay(active ? 3000 : 600, _life.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void ReplaceProcesses(ObservableCollection<ProcessRow> target,
        IReadOnlyList<ProcessUsage> source, bool cpu, ulong totalMemoryBytes)
    {
        var trends = cpu ? _cpuTrends : _memoryTrends;
        var visibleIds = source.Select(item => item.Pid).ToHashSet();
        foreach (var oldPid in trends.Keys.Where(pid => !visibleIds.Contains(pid)).ToArray())
            trends.Remove(oldPid);
        foreach (var process in source)
        {
            if (!trends.TryGetValue(process.Pid, out var history))
                trends[process.Pid] = history = [];
            history.Add(cpu ? process.CpuPercent : process.WorkingSetBytes);
            if (history.Count > 20) history.RemoveAt(0);
        }
        var peak = Math.Max(source.Select(p => cpu ? p.CpuPercent : p.WorkingSetBytes)
            .DefaultIfEmpty(0).Max(), trends.Values.SelectMany(x => x).DefaultIfEmpty(0).Max());
        target.Clear();
        for (var i = 0; i < source.Count; i++)
        {
            var process = source[i];
            var measure = cpu ? process.CpuPercent : process.WorkingSetBytes;
            var value = cpu ? $"{process.CpuPercent.ToString("0.#", English)}%"
                : FormatBytes(process.WorkingSetBytes);
            var context = cpu ? "CPU" : totalMemoryBytes > 0
                ? $"{(process.WorkingSetBytes / (double)totalMemoryBytes * 100).ToString("0.#", English)}% RAM"
                : "RAM";
            var (line, area) = BuildTrend(trends[process.Pid], peak);
            target.Add(new ProcessRow((i + 1).ToString("00", English), process.Name,
                $"PID {process.Pid}", value, context, peak > 0 ? measure / peak * 100 : 0,
                line, area, cpu ? CpuAccent : MemoryAccent));
        }
    }

    private static (PointCollection Line, PointCollection Area) BuildTrend(
        IReadOnlyList<double> samples, double peak)
    {
        var line = new PointCollection();
        var values = samples.Count == 1 ? new[] { samples[0], samples[0] } : samples;
        for (var i = 0; i < values.Count; i++)
        {
            var x = i * 120d / (values.Count - 1);
            var y = 36d - (peak > 0 ? Math.Clamp(values[i] / peak, 0, 1) * 29d : 0d);
            line.Add(new Point(x, y));
        }
        var area = new PointCollection { new(0, 40) };
        foreach (var point in line) area.Add(point);
        area.Add(new Point(120, 40));
        line.Freeze();
        area.Freeze();
        return (line, area);
    }

    private static Brush MakeAccent(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static void Replace(ObservableCollection<SensorReading> target, IEnumerable<SensorReading> source)
    { target.Clear(); foreach (var reading in source) target.Add(reading); }

    private static string Primary(IEnumerable<SensorReading> readings, string[] preferred)
    {
        var list = readings.ToList();
        foreach (var term in preferred)
        {
            var match = list.FirstOrDefault(x => x.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return $"{Math.Round(match.Value, MidpointRounding.AwayFromZero):0} °C";
        }
        return list.Count > 0 ? $"{Math.Round(list[0].Value, MidpointRounding.AwayFromZero):0} °C" : Unavailable;
    }

    private static SensorReading? PrimaryGpuLoad(IEnumerable<SensorReading> readings)
    {
        var list = readings.ToList();
        return list.FirstOrDefault(x => x.Name.Contains("core", StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(x => x.Name.Contains("total", StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault();
    }

    private static string CompactReading(string value, string suffixToRemove, string suffix)
    {
        if (!value.EndsWith(suffixToRemove, StringComparison.Ordinal))
            return value switch
            {
                "Collecting data…" => "…",
                "Unavailable" or "Not available" => "N/A",
                _ => value
            };
        var numericPart = value[..^suffixToRemove.Length].Trim();
        return double.TryParse(numericPart, NumberStyles.Float, English, out var number)
            ? $"{Math.Round(number, MidpointRounding.AwayFromZero):0}{suffix}"
            : "N/A";
    }

    private async Task ScanAsync()
    {
        Busy = true;
        _operation = new CancellationTokenSource();
        CleanupStatus = "Scanning temporary files…";
        CleanupResult = "";
        Candidates.Clear();
        _scan = null;
        try
        {
            var result = await _cleanup.ScanAsync(_operation.Token);
            _scan = result;
            foreach (var item in result.Candidates.OrderBy(x => x.Root).ThenBy(x => x.RelativePath))
                Candidates.Add(item);
            Estimated = FormatBytes(result.EstimatedBytes);
            CleanupStatus = $"Review Results · {result.Candidates.Count:N0} eligible file(s) · {result.Skipped:N0} skipped during scan";
            if (result.Warnings.Count > 0) CleanupStatus += " · Some folders were unavailable.";
        }
        catch (OperationCanceledException) { CleanupStatus = "Scan cancelled."; }
        catch (Exception ex) { CleanupStatus = "The scan could not be completed."; LocalLog.Error("cleanup scan", ex); }
        finally { _operation.Dispose(); _operation = null; Busy = false; }
    }

    private void AddLargeFolder()
    {
        string? chosen;
        try { chosen = PickLargeFolder?.Invoke(); }
        catch (Exception ex)
        { LargeFilesStatus = "The folder picker could not be opened."; LocalLog.Error("large file folder picker", ex); return; }
        if (string.IsNullOrWhiteSpace(chosen)) return;

        if (!_largeFiles.IsSafeRoot(chosen, out var reason))
        { LargeFilesStatus = reason; return; }

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(chosen));
        if (LargeFileLocations.Any(x => PathsOverlap(x.Path, fullPath)))
        { LargeFilesStatus = "That folder is already included or overlaps another selected folder."; return; }

        LargeFileLocations.Add(new ScanLocationOption(Path.GetFileName(fullPath), fullPath, true));
        LargeFilesStatus = "Folder added. Search when you are ready.";
    }

    private async Task SearchLargeFilesAsync()
    {
        var roots = LargeFileLocations.Where(x => x.IsSelected).Select(x => x.Path).ToArray();
        if (roots.Length == 0)
        { LargeFilesStatus = "Select at least one personal folder to search."; return; }

        Busy = true;
        IsLargeFileScanning = true;
        _operation = new CancellationTokenSource();
        LargeFilesStatus = "Searching selected folders…";
        LargeFilesProgressText = "Preparing scan…";
        LargeFilesActionResult = "";
        LargeFileItems.Clear();
        Notify(nameof(LargeFilesSelectedSummary));
        try
        {
            var progress = new Progress<LargeFileScanProgress>(update =>
            {
                var location = string.IsNullOrWhiteSpace(update.CurrentDirectory)
                    ? ""
                    : $"  ·  {Path.GetFileName(Path.TrimEndingDirectorySeparator(update.CurrentDirectory))}";
                LargeFilesProgressText = $"{update.ItemsVisited:N0} items checked  ·  {update.DirectoriesVisited:N0} folders visited  ·  {update.CandidatesFound:N0} matches{location}";
            });
            var result = await _largeFiles.ScanAsync(roots,
                (long)LargeFileMinimumMegabytes * 1024 * 1024, LargeFileAgeDays, progress, _operation.Token);
            foreach (var candidate in result.Candidates)
            {
                var item = new LargeFileItem(candidate);
                item.PropertyChanged += LargeFileSelectionChanged;
                LargeFileItems.Add(item);
            }
            LargeFilesStatus = result.Candidates.Count == 0
                ? $"No files matched the selected size and age filters. {result.Skipped:N0} item(s) skipped."
                : $"Found {result.Candidates.Count:N0} file(s) · {result.Skipped:N0} item(s) skipped · modified more than {LargeFileAgeDays} days ago.";
            if (result.Warnings.Count > 0) LargeFilesStatus += " Some folders could not be fully scanned.";
            Notify(nameof(LargeFilesSelectedSummary));
        }
        catch (OperationCanceledException)
        { LargeFilesStatus = "Search cancelled."; }
        catch (Exception ex)
        { LargeFilesStatus = "The search could not be completed."; LocalLog.Error("large file search", ex); }
        finally { _operation.Dispose(); _operation = null; IsLargeFileScanning = false; Busy = false; }
    }

    private async Task MoveLargeFilesAsync()
    {
        var selectedItems = LargeFileItems.Where(x => x.IsSelected).ToArray();
        var selected = selectedItems.Select(x => x.Candidate).ToArray();
        var selectedBytes = selected.Sum(x => x.Length);
        if (selected.Length == 0 || ConfirmLargeFiles?.Invoke(selected.Length, selectedBytes) != true) return;

        Busy = true;
        _operation = new CancellationTokenSource();
        LargeFilesStatus = "Moving selected files to the Recycle Bin…";
        try
        {
            var result = await _largeFiles.MoveToRecycleBinAsync(selected, _operation.Token);
            var movedPaths = new HashSet<string>(result.MovedPaths, StringComparer.OrdinalIgnoreCase);
            foreach (var item in selectedItems.Where(x => movedPaths.Contains(Path.GetFullPath(x.Candidate.FullPath))).ToArray())
            {
                item.PropertyChanged -= LargeFileSelectionChanged;
                LargeFileItems.Remove(item);
            }
            LargeFilesActionResult = $"Moved to Recycle Bin: {result.Moved:N0} file(s) · {FormatBytes(result.MovedBytes)} · Skipped: {result.Skipped:N0}";
            LargeFilesStatus = result.Cancelled
                ? "Action cancelled. Successfully moved files were removed from this list."
                : result.Moved > 0
                    ? "Moved files were removed from the results. Files in the Recycle Bin can be restored."
                    : "No files were moved. The selected results remain available for review.";
            Notify(nameof(LargeFilesSelectedSummary));
            CommandManager.InvalidateRequerySuggested();
        }
        catch (OperationCanceledException)
        { LargeFilesStatus = "Action cancelled. Some files may already have been moved; search again to refresh results."; }
        catch (Exception ex)
        { LargeFilesStatus = "Some files could not be moved. Search again to refresh results."; LocalLog.Error("large file cleanup", ex); }
        finally { _operation.Dispose(); _operation = null; Busy = false; }
    }

    private void LargeFileSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LargeFileItem.IsSelected))
        {
            Notify(nameof(LargeFilesSelectedSummary));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void SetLargeFileSelection(bool selected)
    {
        foreach (var item in LargeFileItems) item.IsSelected = selected;
        Notify(nameof(LargeFilesSelectedSummary));
    }

    private static bool PathsOverlap(string first, string second)
    {
        first = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        second = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase) ||
            first.StartsWith(second + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            second.StartsWith(first + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async Task CleanAsync()
    {
        if (_scan is null || ConfirmClean?.Invoke(_scan) != true) return;
        Busy = true;
        _operation = new CancellationTokenSource();
        CleanupStatus = "Deleting temporary files…";
        try
        {
            var result = await _cleanup.CleanAsync(_scan, _operation.Token);
            CleanupResult = $"Space freed: {FormatBytes(result.FreedBytes)}   ·   Files deleted: {result.Deleted:N0}   ·   Files skipped: {result.Skipped:N0}";
            CleanupStatus = result.Skipped > 0 ? "Cleanup complete. Some files were busy, changed, or unavailable." : "Cleanup complete.";
            Candidates.Clear(); _scan = null; Estimated = "0 B";
        }
        catch (OperationCanceledException)
        { CleanupStatus = "Cleanup cancelled. Some files may already have been deleted. Scan again to refresh the results."; Candidates.Clear(); _scan = null; }
        catch (Exception ex)
        { CleanupStatus = "Cleanup could not be completed. Scan again to refresh the results."; Candidates.Clear(); _scan = null; LocalLog.Error("cleanup", ex); }
        finally { _operation.Dispose(); _operation = null; Busy = false; }
    }

    private void ToggleStartup()
    {
        try
        {
            _startup.SetEnabled(!StartupEnabled);
            StartupEnabled = _startup.IsEnabled;
            Notify(nameof(StartupLabel));
        }
        catch (Exception ex)
        { CleanupStatus = ex.Message; LocalLog.Error("startup setting", ex); }
    }

    public static string FormatBytes(long value) => value >= 1073741824
        ? $"{(value / 1073741824d).ToString("0.##", English)} GiB"
        : value >= 1048576 ? $"{(value / 1048576d).ToString("0.##", English)} MiB"
        : value >= 1024 ? $"{(value / 1024d).ToString("0.##", English)} KiB" : $"{value} B";

    public static string FormatBytes(ulong value) => FormatBytes((long)Math.Min(value, (ulong)long.MaxValue));

    public void Dispose()
    {
        _life.Cancel(); _operation?.Cancel();
        _sensors.Dispose();
        _life.Dispose();
    }
}

public sealed record ProcessRow(string Rank, string Name, string PidLabel, string Value,
    string Context, double RelativePercent, PointCollection TrendPoints,
    PointCollection AreaPoints, Brush Accent);

public sealed record FilterOption(string Label, int Value);

public sealed class ScanLocationOption : ViewModelBase
{
    private bool _isSelected;

    public ScanLocationOption(string name, string path, bool isSelected)
    { Name = name; Path = path; _isSelected = isSelected; }

    public string Name { get; }
    public string Path { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class LargeFileItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public LargeFileItem(CleanupCandidate candidate) => Candidate = candidate;
    public CleanupCandidate Candidate { get; }
    public string RelativePath => Candidate.RelativePath;
    public string Root => Candidate.Root;
    public string Size => MainViewModel.FormatBytes(Candidate.Length);
    public string LastModified => Candidate.LastWriteUtc.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("en-GB"));
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
