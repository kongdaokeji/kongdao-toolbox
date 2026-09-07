namespace TubaWinUi3.Models;

public sealed class HardwareDetailData
{
    public CpuDetail? Cpu { get; init; }
    public MotherboardDetail? Motherboard { get; init; }
    public MemoryDetail Memory { get; init; } = new();
    public List<GpuDetail> Gpus { get; init; } = [];
    public List<DiskDetail> Disks { get; init; } = [];
    public List<DisplayDetail> Displays { get; init; } = [];
    public List<SoundDetail> SoundDevices { get; init; } = [];
    public List<NetworkDetail> NetworkAdapters { get; init; } = [];
    public NpuDetail? Npu { get; init; }
}

public sealed class CpuDetail
{
    public string? Name { get; set; }
    public string? CodeName { get; set; }
    public string? Package { get; set; }
    public int Cores { get; set; }
    public int Threads { get; set; }
    public string? MaxClockSpeed { get; set; }
    public string? CurrentClockSpeed { get; set; }
    public string? L2CacheSize { get; set; }
    public string? L3CacheSize { get; set; }
    public string? ExtClock { get; set; }
    public string? Architecture { get; set; }
    public string? Manufacturer { get; set; }
    public string? ProcessorId { get; set; }
    public string? BrandKey { get; set; }
    public bool IsVerified { get; set; }
}

public sealed class MotherboardDetail
{
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Version { get; set; }
    public string? Chipset { get; set; }
    public string? BiosBrand { get; set; }
    public string? BiosVersion { get; set; }
    public string? BiosDate { get; set; }
    public bool IsVerified { get; set; }
}

public sealed class MemoryDetail
{
    public string? TotalCapacity { get; set; }
    public string? MemoryType { get; set; }
    public string? ChannelMode { get; set; }
    public int TotalSlots { get; set; }
    public int UsedSlots { get; set; }
    public List<MemoryModuleDetail> Modules { get; init; } = [];
}

public sealed class MemoryModuleDetail
{
    public string? Designation { get; set; }
    public string? Capacity { get; set; }
    public string? Speed { get; set; }
    public string? RatedSpeed { get; set; }
    public string? Manufacturer { get; set; }
    public string? PartNumber { get; set; }
    public string? Type { get; set; }
    public string? FormFactor { get; set; }
}

public sealed class GpuDetail
{
    public string? Name { get; set; }
    public string? GpuCode { get; set; }
    public string? AdapterRAM { get; set; }
    public string? MemorySize { get; set; }
    public string? MemoryType { get; set; }
    public string? MemoryBus { get; set; }
    public string? DriverVersion { get; set; }
    public string? DriverDate { get; set; }
    public string? VideoProcessor { get; set; }
    public string? CurrentResolution { get; set; }
    public string? CurrentRefreshRate { get; set; }
    public string? DeviceId { get; set; }
    public string? BrandKey { get; set; }
    public bool IsVerified { get; set; }
}

public sealed class DiskDetail
{
    public string? Model { get; set; }
    public string? MediaType { get; set; }
    public string? Size { get; set; }
    public string? InterfaceType { get; set; }
    public string? FirmwareRevision { get; set; }
    public string? SerialNumber { get; set; }
    public float? Temperature { get; set; }
    public List<PartitionDetail> Partitions { get; init; } = [];
}

public sealed class PartitionDetail
{
    public string? Name { get; set; }
    public string? DriveLetter { get; set; }
    public string? FileSystem { get; set; }
    public string? Size { get; set; }
    public string? FreeSpace { get; set; }
}

public sealed class DisplayDetail
{
    public string? Name { get; set; }
    public string? Resolution { get; set; }
    public string? RefreshRate { get; set; }
    public bool IsPrimary { get; set; }
    public string? DiagonalInches { get; set; }
}

public sealed class SoundDetail
{
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? Status { get; set; }
}

public sealed class NetworkDetail
{
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? MacAddress { get; set; }
    public string? Speed { get; set; }
    public string? AdapterType { get; set; }
}

public sealed class NpuDetail
{
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? DriverVersion { get; set; }
    public string? DriverDate { get; set; }
    public string? DeviceId { get; set; }

    /// <summary>算力（如 "48 TOPS"），通过型号查 NpuCatalog 得到，未知为 null。</summary>
    public string? ComputeCapability { get; set; }
}
