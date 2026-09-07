using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class CpuzInfoServiceTests
{
    [Fact]
    public void ParseTabLine_ValidTabSeparated_ReturnsKeyValue()
    {
        var result = CpuzInfoService.ParseTabLine("Name\tIntel Core i7");
        Assert.NotNull(result);
        Assert.Equal("Name", result.Value.Key);
        Assert.Equal("Intel Core i7", result.Value.Value);
    }

    [Fact]
    public void ParseTabLine_MultipleTabs_SkipsExtraTabs()
    {
        var result = CpuzInfoService.ParseTabLine("Key\t\tValue");
        Assert.NotNull(result);
        Assert.Equal("Key", result.Value.Key);
        Assert.Equal("Value", result.Value.Value);
    }

    [Fact]
    public void ParseTabLine_NoTab_ReturnsNull()
    {
        Assert.Null(CpuzInfoService.ParseTabLine("NoTabHere"));
    }

    [Fact]
    public void ParseTabLine_EmptyKey_ReturnsNull()
    {
        Assert.Null(CpuzInfoService.ParseTabLine("\tValue"));
    }

    [Fact]
    public void ParseTabLine_EmptyValue_ReturnsNull()
    {
        Assert.Null(CpuzInfoService.ParseTabLine("Key\t"));
    }

    [Fact]
    public void ParseTabLine_WhitespaceLine_ReturnsNull()
    {
        Assert.Null(CpuzInfoService.ParseTabLine("   "));
    }

    [Fact]
    public void ParseReport_CpuSection_ParsesNameAndCores()
    {
        var content = "Processors Information\n\nName\tIntel(R) Core(TM) i7-12700K\nCores\t12\nNumber of Threads\t20\nCodename\tAlder Lake\nPackage\tSocket 1700\n\nChipset\n\nNorthbridge\tIntel Z690\nMemory Type\tDDR5\nMemory Size\t32768 MBytes\n";
        var info = CpuzInfoService.ParseReport(content);
        Assert.Equal("Intel(R) Core(TM) i7-12700K", info.CpuName);
        Assert.Equal("Alder Lake", info.CpuCodeName);
        Assert.Equal("Socket 1700", info.CpuPackage);
        Assert.Equal(20, info.CpuThreads);
    }

    [Fact]
    public void ParseReport_ChipsetSection_ParsesMemoryInfo()
    {
        var content = "Chipset\n\nNorthbridge\tIntel Z690\nMemory Type\tDDR5\nMemory Size\t32768 MBytes\nMemory Frequency\t4800 MHz\nChannels\tDual\n";
        var info = CpuzInfoService.ParseReport(content);
        Assert.Equal("DDR5", info.MemoryType);
        Assert.Equal("32768 MBytes", info.MemorySize);
        Assert.Equal("4800 MHz", info.MemorySpeed);
        Assert.Equal("Dual", info.MemoryChannel);
    }

    [Fact]
    public void ParseReport_DmiBiosSection_ParsesBiosInfo()
    {
        var content = "DMI BIOS\n\nVendor\tAmerican Megatrends International, LLC.\nVersion\t1.20\n\nDMI Baseboard\n\nVendor\tASUSTeK COMPUTER INC.\nModel\tPRIME Z690-A\n";
        var info = CpuzInfoService.ParseReport(content);
        Assert.Equal("American Megatrends International, LLC.", info.BiosBrand);
        Assert.Equal("1.20", info.BiosVersion);
        Assert.Equal("ASUSTeK COMPUTER INC.", info.BoardManufacturer);
        Assert.Equal("PRIME Z690-A", info.BoardModel);
    }

    [Fact]
    public void ParseReport_GpuSection_ParsesGpuInfo()
    {
        var content = "Display Adapters\n\nName\tNVIDIA GeForce RTX 3080\nGPU\tGA102\nMemory Size\t10240 MB\nMemory Type\tGDDR6X\nMemory Bus Width\t320 bit\nDriver Version\t31.0.15.3699\nDevice ID\t10DE 2206\n\nName\tIntel UHD Graphics 770\nGPU\tADL-S GT1\n";
        var info = CpuzInfoService.ParseReport(content);
        Assert.Equal(2, info.Gpus.Count);
        Assert.Equal("NVIDIA GeForce RTX 3080", info.Gpus[0].Name);
        Assert.Equal("GA102", info.Gpus[0].GpuCode);
        Assert.Equal("10240 MB", info.Gpus[0].MemorySize);
        Assert.Equal("Intel UHD Graphics 770", info.Gpus[1].Name);
    }

    [Fact]
    public void ParseReport_DmiMemDeviceSection_ParsesMemDevices()
    {
        var content = "DMI Memory Device\n\nDesignation\tDIMM_A0\nType\tDDR5\nSize\t16384 MB\nSpeed\t4800 MHz\nManufacturer\tKingston\nPart Number\tKF548C38BBK2-32\n\nDMI Memory Device\n\nDesignation\tDIMM_B0\nType\tDDR5\nSize\t16384 MB\nSpeed\t4800 MHz\nManufacturer\tKingston\nPart Number\tKF548C38BBK2-32\n";
        var info = CpuzInfoService.ParseReport(content);
        Assert.Equal(2, info.MemDevices.Count);
        Assert.Equal("DIMM_A0", info.MemDevices[0].Designation);
        Assert.Equal("DDR5", info.MemDevices[0].Type);
        Assert.Equal("Kingston", info.MemDevices[0].Manufacturer);
    }

    [Fact]
    public void ParseReport_EmptyContent_ReturnsDefaultInfo()
    {
        var info = CpuzInfoService.ParseReport("");
        Assert.Null(info.CpuName);
        Assert.Equal(0, info.CpuCores);
        Assert.Empty(info.Gpus);
        Assert.Empty(info.MemDevices);
    }

    [Fact]
    public void ParseReport_SpecificationFillsName_WhenNameEmpty()
    {
        var content = "Processors Information\n\nSpecification\tAMD Ryzen 9 7950X\n";
        var info = CpuzInfoService.ParseReport(content);
        Assert.Equal("AMD Ryzen 9 7950X", info.CpuName);
    }

    [Fact]
    public void ParseReport_NameTakesPrecedence_OverSpecification()
    {
        var content = "Processors Information\n\nName\tAMD Ryzen 9 7950X 16-Core\nSpecification\tAMD Ryzen 9 7950X\n";
        var info = CpuzInfoService.ParseReport(content);
        Assert.Equal("AMD Ryzen 9 7950X 16-Core", info.CpuName);
    }

    [Fact]
    public void ParseReport_CoresWithParentheses_ParsesCorrectly()
    {
        var content = "Processors Information\n\nNumber of Cores\t8 (2P+6E)\nNumber of Threads\t16\n";
        var info = CpuzInfoService.ParseReport(content);
        Assert.Equal(8, info.CpuCores);
        Assert.Equal(16, info.CpuThreads);
    }
}
