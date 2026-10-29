using System.Diagnostics;
using TubaWinUi3.Services.AiPlayground;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// GenerationGate：本地模型安全卸载的基石——“取消在途操作 + 有界等待其退出”的语义。
/// 回归背景：生成中直接 Dispose 模型会让生成线程在已释放的 ORT 对象上继续跑（原生崩溃）。
/// </summary>
public class GenerationGateTests
{
    [Fact]
    public async Task EnterExit_ThenCancelAndWait_ReturnsIdle()
    {
        using var gate = new GenerationGate();
        await gate.EnterAsync(CancellationToken.None);
        gate.Exit();

        Assert.True(gate.CancelAndWait(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task CancelAndWait_WaitsForInFlight_UntilReleased()
    {
        using var gate = new GenerationGate();
        await gate.EnterAsync(CancellationToken.None);

        var waitTask = Task.Run(() => gate.CancelAndWait(TimeSpan.FromSeconds(5)));
        await Task.Delay(150);
        Assert.False(waitTask.IsCompleted); // 在途未退出 → 仍在等待

        gate.Exit();
        Assert.True(await waitTask);
    }

    [Fact]
    public async Task CancelAndWait_TimesOut_WhenStillBusy()
    {
        using var gate = new GenerationGate();
        await gate.EnterAsync(CancellationToken.None);

        var sw = Stopwatch.StartNew();
        Assert.False(gate.CancelAndWait(TimeSpan.FromMilliseconds(150)));
        Assert.True(sw.ElapsedMilliseconds >= 100);

        gate.Exit();
    }

    [Fact]
    public async Task Cancel_CancelsLifetime_And_EnterFails()
    {
        using var gate = new GenerationGate();
        var canceled = false;
        using var registration = gate.Lifetime.Register(() => canceled = true);

        Assert.True(gate.CancelAndWait(TimeSpan.FromSeconds(1)));
        Assert.True(canceled);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => gate.EnterAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Reset_AfterCancel_AllowsEnterAgain()
    {
        using var gate = new GenerationGate();
        Assert.True(gate.CancelAndWait(TimeSpan.FromSeconds(1)));

        gate.Reset();
        await gate.EnterAsync(CancellationToken.None);
        gate.Exit();
    }

    [Fact]
    public async Task EnterAsync_UserCancellation_Throws()
    {
        using var gate = new GenerationGate();
        await gate.EnterAsync(CancellationToken.None); // 先占用

        using var cts = new CancellationTokenSource();
        var waiting = gate.EnterAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        gate.Exit();
    }

    [Fact]
    public void Dispose_ThenCancelAndWait_ReturnsTrue()
    {
        var gate = new GenerationGate();
        gate.Dispose();

        Assert.True(gate.CancelAndWait(TimeSpan.FromSeconds(1)));
    }
}
