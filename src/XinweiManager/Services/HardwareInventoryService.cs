using System.Management;
using XinweiManager.Models;

namespace XinweiManager.Services;

public sealed class HardwareInventoryService : IHardwareInventoryService
{
    public HardwareInventory Read()
    {
        var sections = new List<HardwareSection>
        {
            Query("CPU", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor",
                x => $"{Value(x, "Name")} · {Value(x, "NumberOfCores")} cores · {Value(x, "NumberOfLogicalProcessors")} threads"),
            Query("GPU", "SELECT Name FROM Win32_VideoController", x => Value(x, "Name")),
            Query("Memory", "SELECT Capacity, Speed, Manufacturer FROM Win32_PhysicalMemory",
                x => $"{Bytes(x, "Capacity")} · {Value(x, "Speed")} MHz · {Value(x, "Manufacturer")}"),
            Query("Storage", "SELECT Model, Size FROM Win32_DiskDrive",
                x => $"{Value(x, "Model")} · {Bytes(x, "Size")}"),
            Query("Motherboard", "SELECT Manufacturer, Product FROM Win32_BaseBoard",
                x => $"{Value(x, "Manufacturer")} {Value(x, "Product")}"),
            Query("Windows", "SELECT Caption, Version, OSArchitecture FROM Win32_OperatingSystem",
                x => $"{Value(x, "Caption")} · {Value(x, "Version")} · {Value(x, "OSArchitecture")}")
        };
        return new HardwareInventory(sections, DateTimeOffset.Now);
    }

    private static HardwareSection Query(string title, string query, Func<ManagementBaseObject, string> format)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            var lines = new List<string>();
            foreach (ManagementBaseObject item in results)
            {
                using (item) lines.Add(format(item));
            }
            return new HardwareSection(title, lines.Count > 0 ? lines : ["Not reported"]);
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return new HardwareSection(title, ["Not reported"]);
        }
    }

    private static string Value(ManagementBaseObject item, string key) =>
        item[key]?.ToString() is { Length: > 0 } value ? value.Trim() : "Not reported";

    private static string Bytes(ManagementBaseObject item, string key) =>
        ulong.TryParse(item[key]?.ToString(), out var bytes) ? $"{bytes / 1073741824d:0.##} GiB" : "Not reported";
}
