using System.Diagnostics;
using TubaWinUi3.Services.FileLock;

namespace TubaWinUi3.Tests;

/// <summary>
/// 句柄表快照的生命周期。旧实现只在「所有分片都已收尾」时释放快照，而取消（普通操作！）
/// 与看门狗放弃都会带着仍在跑的分片返回——整块快照连同分片线程一起漏掉。
/// 快照所有权移进 ScanState 后，主线程与最后退出的分片都能把它收回去。
///
/// 断言一律用**增量**：真正永久卡死的分片是既有设计接受的残留（见 RunWithWatchdog），
/// 同一进程里可能正躺着一条，绝对数值会被它污染。
/// </summary>
public class FileLockSnapshotLifetimeTests : IDisposable
{
    /// <summary>放行被钩子挡住的注入点；用例中途失败也要放行，否则分片会一直挂在那里。</summary>
    private ManualResetEventSlim? _gate;

    public void Dispose()
    {
        _gate?.Set();
        FileLockService.TestPerHandleHook = null;
    }

    [Fact]
    public void NormalScan_LeavesNoSnapshotOrRunnerBehind()
    {
        int runnersBefore = FileLockService.ActiveScanRunners;
        int buffersBefore = FileLockService.LiveSnapshotBuffers;

        var result = FileLockService.Scan(Path.GetTempPath(), CancellationToken.None);
        Assert.True(result.Succeeded, $"扫描失败：{result.Error} / {result.ErrorDetail}");

        // 真有句柄卡死（GuardedHandles / Truncated）时，快照按设计留在原地（分片线程还持有指针），
        // 本用例只约束「没有放弃任何分片」的正常扫描。
        if (result.Truncated || result.GuardedHandles > 0) return;

        Assert.Equal(runnersBefore, FileLockService.ActiveScanRunners);
        Assert.Equal(buffersBefore, FileLockService.LiveSnapshotBuffers);
    }

    [Fact]
    public void HandleTableOverCap_ReportsReasonCodeInsteadOfThrowing()
    {
        // 解析阶段（ResolveFileTypeIndex）的第一次句柄表查询就可能撞上限：它必须折成
        // 带原因码的结果，而不是把 ScanFailureException 抛出 Scan——否则界面只能贴异常消息，
        // 英文界面里就会出现中文散文（review 发现 ④ 的主要失败路径）。
        int previous = FileLockService.MaxSnapshotBufferBytes;
        FileLockService.MaxSnapshotBufferBytes = 1024;   // 小到第一次查询必然超限
        try
        {
            var result = FileLockService.Scan(Path.GetTempPath(), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(FileLockScanError.Failed, result.Error);
            Assert.Equal(FileLockScanFailure.HandleTableTooLarge, result.Failure);
            Assert.True(result.FailureCode > 1024, $"FailureCode 应带上需要的字节数，实际 {result.FailureCode}");
        }
        finally
        {
            FileLockService.MaxSnapshotBufferBytes = previous;
        }
    }

    [Fact]
    public void CancelledScan_ReleasesSnapshotOnlyAfterRunnersDrain()
    {
        // 钩子把每个分片挡在门后（无条件等待，Dispose 必放行）：取消一定落在「分片都还在跑」
        // 的窗口里。带超时的等待会留下 1/1024 的时序孔——分片起点恰好是取消检查点时，
        // 等待超时后它们会立刻抛取消退出，后面的「前提不成立」断言就会误报。
        _gate = new ManualResetEventSlim(false);
        int handlesSeen = 0;
        FileLockService.TestPerHandleHook = _ =>
        {
            Interlocked.Increment(ref handlesSeen);
            _gate.Wait();
        };

        int runnersBefore = FileLockService.ActiveScanRunners;
        int buffersBefore = FileLockService.LiveSnapshotBuffers;

        using var cts = new CancellationTokenSource();
        var scanTask = FileLockService.ScanAsync(Path.GetTempPath(), cts.Token);

        var startup = Stopwatch.StartNew();
        while (Volatile.Read(ref handlesSeen) == 0 && startup.Elapsed < TimeSpan.FromSeconds(10))
            Thread.Sleep(10);
        Assert.True(Volatile.Read(ref handlesSeen) > 0, "扫描还没真正开始，本用例前提不成立");

        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => scanTask.GetAwaiter().GetResult());

        // 分片还停在钩子里、仍持有快照指针：此刻绝不能释放，否则就是 use-after-free
        Assert.True(FileLockService.ActiveScanRunners > runnersBefore, "分片已提前退出，本用例前提不成立");
        Assert.Equal(buffersBefore + 1, FileLockService.LiveSnapshotBuffers);

        _gate.Set();   // 放行：分片会在下一个取消检查点退出

        var drain = Stopwatch.StartNew();
        while (FileLockService.ActiveScanRunners > runnersBefore && drain.Elapsed < TimeSpan.FromSeconds(15))
            Thread.Sleep(25);

        Assert.True(FileLockService.ActiveScanRunners == runnersBefore,
            "分片放行后仍未退出（可能真的卡在了 NtQueryObject——那是既有的不可回收情形）");

        // 最后一个分片退出时把快照收回去。旧实现走不到这里：取消路径上快照永远漏着。
        Assert.Equal(buffersBefore, FileLockService.LiveSnapshotBuffers);
    }
}
