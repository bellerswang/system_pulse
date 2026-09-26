using System.Diagnostics;
using XinweiManager.Models;

namespace XinweiManager.Services;

public sealed class ProcessMetricsService : IProcessMetricsService
{
    private readonly object _gate = new();
    private Dictionary<int, TimeSpan> _previousCpu = [];
    private long? _previousTimestamp;

    public ProcessSnapshot Sample()
    {
        lock (_gate)
        {
            long now = Stopwatch.GetTimestamp();
            var elapsed = _previousTimestamp is long before
                ? Stopwatch.GetElapsedTime(before, now) : TimeSpan.Zero;
            var currentCpu = new Dictionary<int, TimeSpan>();
            var rows = new List<ProcessUsage>();
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        var pid = process.Id;
                        var name = process.ProcessName;
                        var workingSet = process.WorkingSet64;
                        TimeSpan cpuTime;
                        try { cpuTime = process.TotalProcessorTime; }
                        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                        { cpuTime = TimeSpan.Zero; }
                        currentCpu[pid] = cpuTime;
                        var cpu = _previousCpu.TryGetValue(pid, out var previous)
                            ? CalculateCpuPercent(previous, cpuTime, elapsed, Environment.ProcessorCount) ?? 0
                            : 0;
                        rows.Add(new ProcessUsage(pid, name, cpu, Math.Max(0, workingSet)));
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
                    { /* Processes can exit or deny access between enumeration and sampling. */ }
                }
            }
            var ready = _previousTimestamp is not null && elapsed > TimeSpan.Zero;
            _previousTimestamp = now;
            _previousCpu = currentCpu;
            return new ProcessSnapshot(
                rows.OrderByDescending(x => x.CpuPercent).ThenBy(x => x.Name).Take(8).ToArray(),
                rows.OrderByDescending(x => x.WorkingSetBytes).ThenBy(x => x.Name).Take(8).ToArray(),
                ready, DateTimeOffset.Now);
        }
    }

    public void Reset()
    {
        lock (_gate) { _previousCpu.Clear(); _previousTimestamp = null; }
    }

    public static double? CalculateCpuPercent(TimeSpan previous, TimeSpan current,
        TimeSpan elapsed, int logicalProcessors)
    {
        if (current < previous || elapsed <= TimeSpan.Zero || logicalProcessors <= 0) return null;
        return Math.Clamp((current - previous).TotalMilliseconds /
            (elapsed.TotalMilliseconds * logicalProcessors) * 100d, 0, 100);
    }
}
