using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

// 与 FavoritesServiceTests 等共享全局静态配置(ConfigManager/ToolCatalog)的测试串行执行,避免互相覆盖
[Collection("GlobalConfigTests")]
public class ToolCatalogTests
{
    [Theory]
    [InlineData("CPUZ_x64", "CPUZ_x")]
    [InlineData("CPUZ_x86", "CPUZ_")]
    [InlineData("CPUZ_ARM64", "CPUZ_ARM")]
    [InlineData("HWMonitor64", "HWMonitor")]
    [InlineData("GPUZ32", "GPUZ")]
    [InlineData("Tool_w64", "Tool_w")]
    [InlineData("Tool_Win64", "Tool_Win")]
    [InlineData("Tool_Win32", "Tool_Win")]
    [InlineData("Tool_64", "Tool_")]
    [InlineData("Tool_32", "Tool_")]
    [InlineData("NoArchTool", "NoArchTool")]
    [InlineData("AIDA64", "AIDA")]
    public void StripArchSuffix_RemovesKnownSuffixes(string input, string expected)
    {
        Assert.Equal(expected, ToolCatalog.StripArchSuffix(input));
    }

    [Theory]
    [InlineData("CPUZ_x64", "CPUZ x64")]
    [InlineData("CPUZ_x86", "CPUZ x86")]
    [InlineData("CPUZ_ARM64", "CPUZ ARM64")]
    [InlineData("CPUZ_arm64", "CPUZ ARM64")]
    [InlineData("My_Tool", "My Tool")]
    [InlineData("SimpleTool", "SimpleTool")]
    public void CleanupName_ReplacesUnderscoreArchAndUnderscores(string input, string expected)
    {
        Assert.Equal(expected, ToolCatalog.CleanupName(input));
    }

    [Theory]
    [InlineData("CPUZ_x64", "x64")]
    [InlineData("CPUZ_x86", "x86")]
    [InlineData("CPUZ_ARM64", "ARM64")]
    [InlineData("CPUZ_arm64", "ARM64")]
    [InlineData("HWMonitor64", "x64")]
    [InlineData("GPUZ32", "x86")]
    [InlineData("Tool_w64", "x64")]
    [InlineData("Tool_Win64", "x64")]
    [InlineData("Tool_Win32", "x86")]
    [InlineData("NoArchTool", null)]
    public void DetectArch_DetectsArchitectureFromName(string input, string? expected)
    {
        Assert.Equal(expected, ToolCatalog.DetectArch(input));
    }

    [Theory]
    [InlineData("ARM64", "ARM64")]
    [InlineData("x64", "x64")]
    [InlineData("x86", "x86")]
    [InlineData("Win64", "x64")]
    [InlineData("Win32", "x86")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Unknown", "Unknown")]
    public void FormatArchDisplay_FormatsCorrectly(string? input, string expected)
    {
        Assert.Equal(expected, ToolCatalog.FormatArchDisplay(input));
    }

    [Fact]
    public void PickPreferredArchOption_EmptyList_ReturnsFallback()
    {
        var fallback = new ArchOption { Name = "Default", Path = "C:\\tool.exe", Arch = "" };
        var result = ToolCatalog.PickPreferredArchOption([], fallback);
        Assert.Same(fallback, result);
    }

    [Fact]
    public void PickPreferredArchOption_EmptyList_NoFallback_ReturnsNull()
    {
        var result = ToolCatalog.PickPreferredArchOption([]);
        Assert.Null(result);
    }

    [Fact]
    public void PickPreferredArchOption_SingleOption_ReturnsThatOption()
    {
        var option = new ArchOption { Name = "Tool x86", Path = "C:\\tool_x86.exe", Arch = "x86" };
        var result = ToolCatalog.PickPreferredArchOption([option]);
        Assert.Same(option, result);
    }

    [Fact]
    public void PickPreferredArchOption_MultipleOptions_PicksPreferredArch()
    {
        var x86 = new ArchOption { Name = "Tool x86", Path = "C:\\tool_x86.exe", Arch = "x86" };
        var x64 = new ArchOption { Name = "Tool x64", Path = "C:\\tool_x64.exe", Arch = "x64" };
        var arm64 = new ArchOption { Name = "Tool ARM64", Path = "C:\\tool_arm64.exe", Arch = "ARM64" };

        var result = ToolCatalog.PickPreferredArchOption([x86, x64, arm64]);

        var priority = ToolCatalog.PreferredArchPriority;
        if (priority[0] == "ARM64")
            Assert.Same(arm64, result);
        else if (priority[0] == "x64")
            Assert.Same(x64, result);
    }

    [Fact]
    public void PickPreferredArchOption_NoMatchingArch_ReturnsFirstOption()
    {
        var x86 = new ArchOption { Name = "Tool x86", Path = "C:\\tool_x86.exe", Arch = "x86" };
        var result = ToolCatalog.PickPreferredArchOption([x86]);
        Assert.Same(x86, result);
    }

    [Fact]
    public void PreferredArchPriority_ContainsAtLeastTwoEntries()
    {
        Assert.True(ToolCatalog.PreferredArchPriority.Count >= 2);
    }

    private static ToolItem MakeItem(string name) => new()
    {
        Name = name,
        Category = "Cat",
        Path = $@"C:\tools\{name}.exe",
        RelativePath = name,
        Extension = "EXE"
    };

    [Fact]
    public void ReorderByName_ReordersKnownAndAppendsUnknown()
    {
        var items = new List<ToolItem> { MakeItem("Alpha"), MakeItem("Beta"), MakeItem("Gamma") };

        var result = ToolCatalog.ReorderByName(items, ["Gamma", "Alpha"]);

        Assert.Equal(["Gamma", "Alpha", "Beta"], result.Select(t => t.Name));
    }

    [Fact]
    public void ReorderByName_IsCaseInsensitive()
    {
        var items = new List<ToolItem> { MakeItem("Alpha"), MakeItem("Beta") };

        var result = ToolCatalog.ReorderByName(items, ["beta", "alpha"]);

        Assert.Equal(["Beta", "Alpha"], result.Select(t => t.Name));
    }
}
