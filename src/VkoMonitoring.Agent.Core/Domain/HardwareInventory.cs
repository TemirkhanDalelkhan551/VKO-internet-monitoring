namespace VkoMonitoring.Agent.Core.Domain;

/// <summary>One non-continuous snapshot of a registered Windows computer.</summary>
public sealed record HardwareInventory(
    Guid SchoolId,
    Guid DeviceId,
    Guid LineId,
    string SchemaVersion,
    DateTimeOffset CollectedAtUtc,
    ComputerInventory Computer,
    OperatingSystemInventory OperatingSystem,
    IReadOnlyList<ProcessorInventory> Processors,
    MemoryInventory Memory,
    IReadOnlyList<StorageInventory> Storage,
    IReadOnlyList<GraphicsInventory> Graphics,
    FirmwareInventory Firmware,
    IReadOnlyList<NetworkAdapterInventory> NetworkAdapters);

public sealed record ComputerInventory(string? HostName, string? Manufacturer, string? Model, string? SerialNumber, string? SystemUuid);
public sealed record OperatingSystemInventory(string? Name, string? Edition, string? Version, string? Build, string? Architecture, DateTimeOffset? InstalledAtUtc);
public sealed record ProcessorInventory(string? Manufacturer, string? Name, int? PhysicalCores, int? LogicalProcessors, int? MaximumClockMhz);
public sealed record MemoryInventory(long? TotalBytes, int? SlotCount, IReadOnlyList<MemoryModuleInventory> Modules);
public sealed record MemoryModuleInventory(string? Manufacturer, string? PartNumber, string? SerialNumber, long? CapacityBytes, int? SpeedMhz);
public sealed record StorageInventory(string? Model, string? Manufacturer, string? SerialNumber, long? CapacityBytes, string? MediaType, string? BusType);
public sealed record GraphicsInventory(string? Name, long? MemoryBytes, string? DriverVersion);
public sealed record FirmwareInventory(string? BaseboardManufacturer, string? BaseboardProduct, string? BiosManufacturer, string? BiosVersion, DateTimeOffset? BiosReleaseDate);
public sealed record NetworkAdapterInventory(string? Name, string? Manufacturer, string? MacAddress, string? AdapterType, bool IsPhysical, bool IsEnabled);
