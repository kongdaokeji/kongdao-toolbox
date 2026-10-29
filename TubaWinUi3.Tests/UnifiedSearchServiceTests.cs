using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class UnifiedSearchServiceTests
{
    [Fact]
    public void CalcScore_ExactMatch_Returns100()
    {
        Assert.Equal(100, UnifiedSearchService.CalcScore("CPU-Z", "CPU-Z", null));
    }

    [Fact]
    public void CalcScore_ExactMatch_CaseInsensitive()
    {
        Assert.Equal(100, UnifiedSearchService.CalcScore("cpu-z", "CPU-Z", null));
    }

    [Fact]
    public void CalcScore_PrefixMatch_Returns80()
    {
        Assert.Equal(80, UnifiedSearchService.CalcScore("CPU", "CPU-Z", null));
    }

    [Fact]
    public void CalcScore_ContainsMatch_Returns60()
    {
        Assert.Equal(60, UnifiedSearchService.CalcScore("Z", "CPU-Z", null));
    }

    [Fact]
    public void CalcScore_SecondaryMatch_Adds20()
    {
        var secondary = new List<string> { "处理器工具", "硬件检测" };
        var score = UnifiedSearchService.CalcScore("处理器", "CPU-Z", secondary);
        Assert.Equal(20, score);
    }

    [Fact]
    public void CalcScore_MultipleSecondaryMatches_AddsPerMatch()
    {
        var secondary = new List<string> { "处理器工具", "硬件工具" };
        var score = UnifiedSearchService.CalcScore("工具", "CPU-Z", secondary);
        Assert.Equal(40, score);
    }

    [Fact]
    public void CalcScore_NoMatch_Returns0()
    {
        Assert.Equal(0, UnifiedSearchService.CalcScore("xyz", "CPU-Z", null));
    }

    [Fact]
    public void CalcScore_NullSecondary_TreatedAsNoSecondary()
    {
        Assert.Equal(60, UnifiedSearchService.CalcScore("PU", "CPU-Z", null));
    }

    [Fact]
    public void CalcScore_EmptySecondary_NoBonus()
    {
        var secondary = new List<string> { "", "  " };
        var score = UnifiedSearchService.CalcScore("test", "Other", secondary);
        Assert.Equal(0, score);
    }

    [Fact]
    public void CalcScore_ExactAndSecondary_Combines()
    {
        var secondary = new List<string> { "CPU-Z Pro" };
        var score = UnifiedSearchService.CalcScore("CPU-Z", "CPU-Z", secondary);
        Assert.Equal(120, score);
    }
}
