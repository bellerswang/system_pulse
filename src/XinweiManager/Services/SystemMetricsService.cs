using System.Runtime.InteropServices;
using XinweiManager.Models;

namespace XinweiManager.Services;

public sealed class SystemMetricsService : ISystemMetricsService
{
    private ulong? _idle, _total;

    public SystemSnapshot Sample()
    {
        double? cpu = null;
        if (GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            var idle = ToUInt64(idleTime);
            var total = ToUInt64(kernelTime) + ToUInt64(userTime);
            if (_idle is not null && _total is not null && total > _total && idle >= _idle)
                cpu = CalculateCpuPercent(_idle.Value, _total.Value, idle, total);
            _idle = idle;
            _total = total;
        }
        else cpu = double.NaN;

        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        bool memoryOk = GlobalMemoryStatusEx(ref memory);
        return new SystemSnapshot(cpu, memoryOk ? memory.MemoryLoad : null,
            memoryOk ? memory.TotalPhysical : null, memoryOk ? memory.AvailablePhysical : null,
            DateTimeOffset.Now);
    }

    public static double? CalculateCpuPercent(ulong oldIdle, ulong oldTotal, ulong newIdle, ulong newTotal)
    {
        if (newTotal <= oldTotal || newIdle < oldIdle) return null;
        var totalDelta = newTotal - oldTotal;
        var idleDelta = newIdle - oldIdle;
        if (idleDelta > totalDelta) return null;
        return Math.Clamp(100d * (1d - (double)idleDelta / totalDelta), 0, 100);
    }

    private static ulong ToUInt64(FileTime time) => ((ulong)time.High << 32) | time.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low, High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
