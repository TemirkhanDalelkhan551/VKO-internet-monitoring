using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Infrastructure.Diagnostics;

/// <summary>Reads WMI once at agent startup. Failures of individual WMI classes leave only that field empty.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHardwareInventoryCollector(AgentOptions options, TimeProvider timeProvider)
    : IHardwareInventoryCollector
{
    public HardwareInventory Collect()
    {
        var computer = First("Win32_ComputerSystemProduct", row => new ComputerInventory(
            Environment.MachineName, Text(row, "Vendor"), Text(row, "Name"), Text(row, "IdentifyingNumber"), Text(row, "UUID")))
            ?? new ComputerInventory(Environment.MachineName, null, null, null, null);
        var os = First("Win32_OperatingSystem", row => new OperatingSystemInventory(
            Text(row, "Caption"), Text(row, "OperatingSystemSKU"), Text(row, "Version"), Text(row, "BuildNumber"), Text(row, "OSArchitecture"), Date(row, "InstallDate")))
            ?? new OperatingSystemInventory(null, null, null, null, null, null);
        var processors = Many("Win32_Processor", row => new ProcessorInventory(
            Text(row, "Manufacturer"), Text(row, "Name"), Int(row, "NumberOfCores"), Int(row, "NumberOfLogicalProcessors"), Int(row, "MaxClockSpeed")));
        var modules = Many("Win32_PhysicalMemory", row => new MemoryModuleInventory(
            Text(row, "Manufacturer"), Text(row, "PartNumber"), Text(row, "SerialNumber"), Long(row, "Capacity"), Int(row, "Speed")));
        var memory = new MemoryInventory(
            FirstValue("Win32_ComputerSystem", row => Long(row, "TotalPhysicalMemory")),
            Count("Win32_PhysicalMemoryArray", "MemoryDevices"), modules);
        var storage = Many("Win32_DiskDrive", row => new StorageInventory(
            Text(row, "Model"), Text(row, "Manufacturer"), Text(row, "SerialNumber"), Long(row, "Size"),
            ClassifyStorage(Text(row, "MediaType"), Text(row, "Model")), Text(row, "InterfaceType")));
        var graphics = Many("Win32_VideoController", row => new GraphicsInventory(
            Text(row, "Name"), Long(row, "AdapterRAM"), Text(row, "DriverVersion")));
        var firmware = new FirmwareInventory(
            First("Win32_BaseBoard", row => Text(row, "Manufacturer")),
            First("Win32_BaseBoard", row => Text(row, "Product")),
            First("Win32_BIOS", row => Text(row, "Manufacturer")),
            First("Win32_BIOS", row => Text(row, "SMBIOSBIOSVersion")) ?? First("Win32_BIOS", row => Text(row, "Version")),
            FirstValue("Win32_BIOS", row => Date(row, "ReleaseDate")));
        var adapters = Many("Win32_NetworkAdapter", row => new NetworkAdapterInventory(
            Text(row, "Name"), Text(row, "Manufacturer"), Text(row, "MACAddress"), Text(row, "AdapterType"),
            IsPhysical(row), Bool(row, "NetEnabled")));

        return new HardwareInventory(options.SchoolId, options.DeviceId, options.LineId, "1.0", timeProvider.GetUtcNow(),
            computer, os, processors, memory, storage, graphics, firmware, adapters);
    }

    private static IReadOnlyList<T> Many<T>(string className, Func<ManagementBaseObject, T> projector)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {className}");
            using var rows = searcher.Get();
            return rows.Cast<ManagementBaseObject>().Select(projector).ToArray();
        }
        catch (ManagementException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static T? First<T>(string className, Func<ManagementBaseObject, T?> projector) where T : class
        => Many(className, projector).FirstOrDefault();

    private static T? FirstValue<T>(string className, Func<ManagementBaseObject, T?> projector) where T : struct
        => Many(className, projector).FirstOrDefault();

    private static int? Count(string className, string property)
        => First(className, row => Int(row, property)?.ToString(CultureInfo.InvariantCulture)) is { } value &&
           int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static string? Text(ManagementBaseObject row, string property) => row[property]?.ToString()?.Trim() is { Length: > 0 } value ? value : null;
    private static int? Int(ManagementBaseObject row, string property) => int.TryParse(Text(row, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static long? Long(ManagementBaseObject row, string property) => long.TryParse(Text(row, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static bool Bool(ManagementBaseObject row, string property) => bool.TryParse(Text(row, property), out var value) && value;
    private static DateTimeOffset? Date(ManagementBaseObject row, string property)
    {
        var value = Text(row, property);
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return ManagementDateTimeConverter.ToDateTime(value).ToUniversalTime(); }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (FormatException) { return null; }
    }

    private static bool IsPhysical(ManagementBaseObject row)
    {
        var name = Text(row, "Name") ?? string.Empty;
        var pnp = Text(row, "PNPDeviceID") ?? string.Empty;
        return !name.Contains("virtual", StringComparison.OrdinalIgnoreCase) &&
               !name.Contains("vpn", StringComparison.OrdinalIgnoreCase) &&
               !pnp.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ClassifyStorage(string? mediaType, string? model)
    {
        var value = $"{mediaType} {model}";
        if (value.Contains("NVMe", StringComparison.OrdinalIgnoreCase)) return "NVMe";
        if (value.Contains("SSD", StringComparison.OrdinalIgnoreCase) || value.Contains("Solid State", StringComparison.OrdinalIgnoreCase)) return "SSD";
        if (value.Contains("HDD", StringComparison.OrdinalIgnoreCase) || value.Contains("Fixed hard disk", StringComparison.OrdinalIgnoreCase)) return "HDD";
        return mediaType;
    }
}
