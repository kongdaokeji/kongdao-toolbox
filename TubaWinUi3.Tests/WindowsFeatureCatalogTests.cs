using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

public class WindowsFeatureCatalogTests
{
    [Fact]
    public void ParseIds_TrimsDeduplicatesAndAcceptsChineseComma()
    {
        Assert.Equal(new uint[] { 48433719, 51243689, uint.MaxValue },
            WindowsFeatureCatalog.ParseIds(" 48433719,51243689，48433719,4294967295 "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("1,,2")]
    [InlineData("1,")]
    [InlineData("-1,2")]
    [InlineData("1,hello")]
    [InlineData("4294967296")]
    [InlineData("+1")]
    public void ParseIds_RejectsWholeInputBeforeAnyWrite(string input)
    {
        Assert.Throws<ArgumentException>(() => WindowsFeatureCatalog.ParseIds(input));
    }

    private static FeatureFlagEntry Entry(uint id, FeatureState state, int priority = 8) =>
        new(id, null, priority, state, false, true);

    [Fact]
    public void Merge_PreservesBothStatesAndHighestPriorityWithoutDuplicateIds()
    {
        var result = WindowsFeatureCatalog.Merge(
            [Entry(1, FeatureState.Disabled, 4), Entry(1, FeatureState.Enabled)],
            [Entry(1, FeatureState.Disabled), Entry(2, FeatureState.Default)],
            new Dictionary<uint, string> { [1] = "Named", [3] = "DictionaryOnly" });
        Assert.Equal(new uint[] { 1, 2, 3 }, result.Select(e => e.FeatureId));
        Assert.Equal(FeatureState.Enabled, result[0].RuntimeState);
        Assert.Equal(FeatureState.Disabled, result[0].BootState);
        Assert.Equal("Named", result[0].Name);
        Assert.Null(result[1].RuntimeState);
        Assert.True(result[1].HasConfig);
        Assert.False(result[2].HasConfig);
        Assert.Null(result[2].BootState);
    }

    [Fact]
    public void Filter_SearchesWholeCatalogIncludingUnnamedAndDictionaryOnlyEntries()
    {
        var result = WindowsFeatureCatalog.Merge([Entry(12, FeatureState.Enabled)], [],
            new Dictionary<uint, string> { [34] = "TaskbarExperiment" });
        Assert.Single(WindowsFeatureCatalog.Filter(result, "", true));
        Assert.Equal(2, WindowsFeatureCatalog.Filter(result, "", false).Count);
        Assert.Equal(34u, Assert.Single(WindowsFeatureCatalog.Filter(result, " TASKBAR ", true)).FeatureId);
        Assert.Equal(12u, Assert.Single(WindowsFeatureCatalog.Filter(result, "12", true)).FeatureId);
        Assert.Empty(WindowsFeatureCatalog.Filter(result, "missing", false));
    }
}
