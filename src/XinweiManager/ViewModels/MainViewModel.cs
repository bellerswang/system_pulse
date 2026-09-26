using System.Collections.ObjectModel;
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
    private readonly IStartupService _startup;
    private readonly Dictionary<int, List<double>> _cpuTrends = [];
    private readonly Dictionary<int, List<double>> _memoryTrends = [];
    private static readonly Brush CpuAccent = MakeAccent(0, 229, 255);
    private static readonly Brush MemoryAccent = MakeAccent(255, 74, 174);
    private readonly CancellationTokenSource _life = new();
    private CancellationTokenSource? _operation;
    private CleanupScanResult? _scan;
    private bool _busy, _windowVisible, _startupEnabled, _cpuExpanded, _memoryExpanded;
    private string _cpuUsage = "Collecting data…", _memoryUsage = "Collecting data…";
    private string _memoryDetail = "", _cpuTemp = "Collecting data…", _gpuTemp = "Collecting data…";
    private string _fansStatus = "Collecting data…", _lastUpdated = "—";
    private string _cleanupStatus = "Scan temporary files to see what can be removed.";
    private string _hardwareStatus = "Open this page to load your hardware details.";
    private string _estimated = "0 B", _cleanupResult = "";
    private string _cpuProcessStatus = "Select to inspect the busiest processes.";
    private string _memoryProcessStatus = "Select to inspect memory use by process.";
    private double _cpuLoad, _memoryLoad;
    private ulong _totalMemoryBytes;

    public MainViewModel(ISystemMetricsService metrics, ISensorService sensors,
        IProcessMetricsService processes,
        IHardwareInventoryService inventory, ICleanupService cleanup, IStartupService startup)
    {
        _metrics = metrics; _sensors = sensors; _processes = processes;
        _inventory = inventory; _cleanup = cleanup; _startup = startup;
        _startupEnabled = startup.IsEnabled;
        ScanCommand = new RelayCommand(() => _ = ScanAsync(), () => !Busy);
        CleanCommand = new RelayCommand(() => _ = CleanAsync(), () => !Busy && _scan?.Candidates.Count > 0);
        CancelCommand = new RelayCommand(() => _operation?.Cancel(), () => Busy);
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

    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) CommandManager.InvalidateRequerySuggested(); } }
    public bool WindowVisible
    {
        get => _windowVisible;
        set { if (Set(ref _windowVisible, value) && !value) _processes.Reset(); }
    }
    public bool CpuExpanded { get => _cpuExpanded; private set { if (Set(ref _cpuExpanded, value)) Notify(nameof(CpuToggleLabel)); } }
    public bool MemoryExpanded { get => _memoryExpanded; private set { if (Set(ref _memoryExpanded, value)) Notify(nameof(MemoryToggleLabel)); } }
    public string CpuToggleLabel => CpuExpanded ? "HIDE PROCESSES  −" : "TOP PROCESSES  +";
    public string MemoryToggleLabel => MemoryExpanded ? "HIDE PROCESSES  −" : "TOP PROCESSES  +";
    public string CpuProcessStatus { get => _cpuProcessStatus; private set => Set(ref _cpuProcessStatus, value); }
    public string MemoryProcessStatus { get => _memoryProcessStatus; private set => Set(ref _memoryProcessStatus, value); }
    public bool StartupEnabled { get => _startupEnabled; private set => Set(ref _startupEnabled, value); }
    public bool StartupAvailable => _startup.IsDeployed;
    public string StartupLabel => StartupEnabled ? "Launch at sign-in: On" : "Launch at sign-in: Off";
    public string CpuUsage { get => _cpuUsage; private set => Set(ref _cpuUsage, value); }
    public double CpuLoad { get => _cpuLoad; private set => Set(ref _cpuLoad, value); }
    public string MemoryUsage { get => _memoryUsage; private set => Set(ref _memoryUsage, value); }
    public double MemoryLoad { get => _memoryLoad; private set => Set(ref _memoryLoad, value); }
    public string MemoryDetail { get => _memoryDetail; private set => Set(ref _memoryDetail, value); }
    public string CpuTemperature { get => _cpuTemp; private set => Set(ref _cpuTemp, value); }
    public string GpuTemperature { get => _gpuTemp; private set => Set(ref _gpuTemp, value); }
    public string FansStatus { get => _fansStatus; private set => Set(ref _fansStatus, value); }
    public string LastUpdated { get => _lastUpdated; private set => Set(ref _lastUpdated, value); }
    public string CleanupStatus { get => _cleanupStatus; private set => Set(ref _cleanupStatus, value); }
    public string Estimated { get => _estimated; private set => Set(ref _estimated, value); }
    public string CleanupResult { get => _cleanupResult; private set => Set(ref _cleanupResult, value); }
    public string HardwareStatus { get => _hardwareStatus; private set => Set(ref _hardwareStatus, value); }
    public static string Unavailable => "Not available";

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
                    ? double.IsNaN(cpu) ? "Unavailable" : $"{cpu.ToString("0.#", English)}%"
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
                FansStatus = Fans.Count == 0 ? Unavailable : $"{Fans.Count} sensor(s)";
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                CpuSensors.Clear(); GpuSensors.Clear(); Fans.Clear();
                CpuTemperature = Unavailable; GpuTemperature = Unavailable; FansStatus = Unavailable;
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
            if (match is not null) return $"{match.Value.ToString("0.#", English)} °C";
        }
        return list.Count > 0 ? $"{list[0].Value.ToString("0.#", English)} °C" : Unavailable;
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
