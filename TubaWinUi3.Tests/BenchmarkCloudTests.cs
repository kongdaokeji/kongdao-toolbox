using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class BenchmarkCloudTests
{
    [Fact]
    public void CacheDuration_IsSixDays()
        => Assert.Equal(TimeSpan.FromDays(6), BenchmarkCloudService.CacheDuration);

    [Fact]
    public void IsCacheFresh_ExpiresAtSixDaysAndRejectsFutureTimestamps()
    {
        var now = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        Assert.True(BenchmarkCloudService.IsCacheFresh(now.AddDays(-5), now));
        Assert.False(BenchmarkCloudService.IsCacheFresh(now.AddDays(-6), now));
        Assert.False(BenchmarkCloudService.IsCacheFresh(now.AddMinutes(1), now));
    }

    [Fact]
    public void GetLeaderboardPageSlice_ReturnsAtMostFiftyEntries()
    {
        var entries = Enumerable.Range(0, 125).ToList();

        Assert.Equal(Enumerable.Range(0, 50), BenchmarkCloudService.GetLeaderboardPageSlice(entries, 0));
        Assert.Equal(Enumerable.Range(50, 50), BenchmarkCloudService.GetLeaderboardPageSlice(entries, 1));
        Assert.Equal(Enumerable.Range(100, 25), BenchmarkCloudService.GetLeaderboardPageSlice(entries, 2));
        Assert.Empty(BenchmarkCloudService.GetLeaderboardPageSlice(entries, 3));
        Assert.Empty(BenchmarkCloudService.GetLeaderboardPageSlice(entries, -1));
    }
}
