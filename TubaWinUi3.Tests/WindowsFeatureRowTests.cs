using TubaWinUi3.Pages;
using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

public class WindowsFeatureRowTests
{
    private static FeatureFlagEntry Entry(FeatureState? current, FeatureState? boot) =>
        new(123, "Example", 8, current ?? FeatureState.Default, false, current is not null || boot is not null)
        { RuntimeState = current, BootState = boot };

    [Theory]
    [InlineData(null, null, false)]
    [InlineData(FeatureState.Enabled, null, true)]
    [InlineData(FeatureState.Enabled, FeatureState.Disabled, false)]
    [InlineData(FeatureState.Disabled, FeatureState.Enabled, true)]
    public void Switch_ReflectsSavedConfiguration(FeatureState? current, FeatureState? boot, bool expected)
    {
        Assert.Equal(expected, new FeatureRowVm(Entry(current, boot), true).IsOn);
    }

    [Fact]
    public void Apply_UpdatesExistingRowAndNotifiesBindings()
    {
        var row = new FeatureRowVm(Entry(null, null), true);
        var notifications = 0;
        row.PropertyChanged += (_, _) => notifications++;
        row.Apply(Entry(FeatureState.Enabled, FeatureState.Enabled), true);
        Assert.True(row.IsOn);
        Assert.Equal("已启用", row.StateText);
        Assert.True(row.CanReset);
        Assert.Equal(1, notifications);
        row.Apply(Entry(null, null), false);
        Assert.False(row.IsOn);
        Assert.False(row.CanToggle);
        Assert.False(row.CanReset);
        Assert.Equal("未配置", row.StateText);
    }
}
