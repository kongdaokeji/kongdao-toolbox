using TubaWinUi3.Models;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Tests;

public class HardwareFocusTests
{
    [Fact]
    public void DiskFocus_ShowsOnlySelectedDeviceAndItsPartitions()
    {
        var data = new HardwareDetailData { Disks = [
            new() { Model = "SSD A", SerialNumber = "SERIAL-A", Temperature = 31 },
            new() { Model = "SSD B", SerialNumber = "SERIAL-B", Temperature = 42,
                Partitions = [new() { DriveLetter = "D:", Size = "1 TB" }] }
        ] };
        var rows = HardwareDetailPage.BuildFocusItems(data, "硬盘", "SSD B (1 TB)", 1);
        Assert.Contains(rows, row => row.Value == "SERIAL-B");
        Assert.Contains(rows, row => row.Value.Contains("42"));
        Assert.Contains(rows, row => row.Value.Contains("D:"));
        Assert.DoesNotContain(rows, row => row.Value.Contains("SERIAL-A") || row.Value == "SSD A");
    }

    [Fact]
    public void NetworkFocus_MatchesNameWhenOverviewFilteredVirtualDevices()
    {
        var data = new HardwareDetailData { NetworkAdapters = [
            new() { Name = "Virtual Ethernet", MacAddress = "VIRTUAL" },
            new() { Name = "Wi-Fi Adapter", MacAddress = "PHYSICAL" }
        ] };
        var rows = HardwareDetailPage.BuildFocusItems(data, "网卡", "Wi-Fi Adapter", 0);
        Assert.Contains(rows, row => row.Value == "PHYSICAL");
        Assert.DoesNotContain(rows, row => row.Value == "VIRTUAL");
    }

    [Fact]
    public void IdenticalDeviceNames_FallBackToClickedIndex()
    {
        var data = new HardwareDetailData { Disks = [
            new() { Model = "Identical SSD", SerialNumber = "FIRST" },
            new() { Model = "Identical SSD", SerialNumber = "SECOND" }
        ] };
        var rows = HardwareDetailPage.BuildFocusItems(data, "硬盘", "Identical SSD", 1);
        Assert.Contains(rows, row => row.Value == "SECOND");
        Assert.DoesNotContain(rows, row => row.Value == "FIRST");
    }

    [Fact]
    public void MemoryFocus_ExcludesOtherDimms()
    {
        var data = new HardwareDetailData { Memory = new() { MemoryType = "DDR5", Modules = [
            new() { PartNumber = "DIMM-A", Capacity = "16 GB", SerialNumber = "MEM-A" },
            new() { PartNumber = "DIMM-B", Capacity = "32 GB", SerialNumber = "MEM-B" }
        ] } };
        var rows = HardwareDetailPage.BuildFocusItems(data, "内存", "DIMM-B 32 GB", 1);
        Assert.Contains(rows, row => row.Value.Contains("MEM-B"));
        Assert.DoesNotContain(rows, row => row.Value.Contains("MEM-A"));
    }

    [Fact]
    public void BiosFocus_ExcludesUnrelatedMotherboardIdentity()
    {
        var data = new HardwareDetailData { Motherboard = new() {
            Model = "Board A", BiosVersion = "1.04", SmbiosVersion = "3.6"
        } };
        var rows = HardwareDetailPage.BuildFocusItems(data, "BIOS", "1.04", 0);
        Assert.Contains(rows, row => row.Value == "1.04");
        Assert.Contains(rows, row => row.Value == "3.6");
        Assert.DoesNotContain(rows, row => row.Value == "Board A");
    }
}
