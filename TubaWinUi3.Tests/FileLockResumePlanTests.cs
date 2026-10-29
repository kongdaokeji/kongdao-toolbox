using TubaWinUi3.Services.FileLock;

namespace TubaWinUi3.Tests;

/// <summary>
/// 卡住分片的续跑决策。旧实现每次判停滞都把「已被放弃、但线程仍卡着没跑完」的分片重新续跑一遍：
/// 同一区间被反复重扫（重复命中只是被 _seen 掩盖），GuardedHandles 也会被重复计数。
/// 被放弃过的分片必须退出候选。
/// </summary>
public class FileLockResumePlanTests
{
    [Fact]
    public void StalledRunner_ResumesAfterItsCurrentIndex()
    {
        Assert.True(FileLockService.TryPlanResume(start: 0, end: 1000, index: 100, abandoned: false, out int from));
        Assert.Equal(101, from);
    }

    [Fact]
    public void AbandonedRunner_IsNotResumedAgain()
    {
        Assert.False(FileLockService.TryPlanResume(start: 0, end: 1000, index: 100, abandoned: true, out _));
    }

    [Fact]
    public void RunnerStuckAtLastHandle_IsNotResumed()
    {
        Assert.False(FileLockService.TryPlanResume(start: 0, end: 1000, index: 999, abandoned: false, out _));
    }

    [Fact]
    public void FreshRunner_ResumesFromItsStart()
    {
        // 刚启动的分片 Index = Start - 1：续跑起点应落在 Start，而不是 Start + 1
        Assert.True(FileLockService.TryPlanResume(start: 500, end: 1000, index: 499, abandoned: false, out int from));
        Assert.Equal(500, from);
    }
}
