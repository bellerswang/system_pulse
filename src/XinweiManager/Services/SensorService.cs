using LibreHardwareMonitor.Hardware;
using XinweiManager.Models;

namespace XinweiManager.Services;

public sealed class SensorService : ISensorService
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true,
        IsControllerEnabled = true
    };
    private bool _opened;
    private readonly object _gate = new();

    public IReadOnlyList<SensorReading> Sample()
    {
        lock (_gate)
        {
            if (!_opened) { _computer.Open(); _opened = true; }
            var readings = new List<SensorReading>();
            foreach (var hardware in _computer.Hardware)
                ReadHardware(hardware, readings);
            return readings;
        }
    }

    private static void ReadHardware(IHardware hardware, List<SensorReading> readings)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
        {
            if (!sensor.Value.HasValue || !IsUsable(sensor.Value.Value))
                continue;
            SensorKind? kind = sensor.SensorType switch
            {
                SensorType.Temperature when IsUsableTemperature(sensor.Value.Value) && hardware.HardwareType == HardwareType.Cpu => SensorKind.CpuTemperature,
                SensorType.Temperature when IsUsableTemperature(sensor.Value.Value) &&
                    (hardware.HardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia) => SensorKind.GpuTemperature,
                SensorType.Fan when sensor.Value.Value >= 0 => SensorKind.Fan,
                _ => null
            };
            if (kind is not null)
                readings.Add(new SensorReading(kind.Value, hardware.Name, sensor.Name,
                    sensor.Value.Value, DateTimeOffset.Now));
        }
        foreach (var child in hardware.SubHardware) ReadHardware(child, readings);
    }

    public static bool IsUsable(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static bool IsUsableTemperature(float value) => IsUsable(value) && value > 0 && value <= 150;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_opened) { _computer.Close(); _opened = false; }
        }
    }
}
