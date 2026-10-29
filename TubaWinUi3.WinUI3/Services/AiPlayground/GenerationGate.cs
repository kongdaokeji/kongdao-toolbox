namespace TubaWinUi3.Services.AiPlayground;

/// <summary>
/// 「单次在途操作」门（纯托管，可单测）：模型加载 / 生成期间，卸载或 Dispose 必须先取消
/// 在途操作并有界等待其退出，再销毁 native 对象——否则生成线程会在已释放的 ORT 模型上
/// 继续跑（use-after-free，原生层直接崩进程，托管 try/catch 拦不住）。
///
/// 用法：在途操作 <see cref="EnterAsync"/>（与调用方取消、生命周期取消联动）→ 结束
/// <see cref="Exit"/>；释放方 <see cref="CancelAndWait"/> 取消生命周期并有界等待空闲，
/// 之后视需要 <see cref="Reset"/> 重新武装（空闲时才能调用）。
/// </summary>
internal sealed class GenerationGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private CancellationTokenSource _lifetime = new();
    private int _disposed;

    /// <summary>生命周期 token：<see cref="CancelAndWait"/> 后所有在途等待立即失效。</summary>
    public CancellationToken Lifetime => _lifetime.Token;

    /// <summary>
    /// 进入在途操作（等待信号量）。调用方取消或生命周期取消都会让等待抛出
    /// <see cref="OperationCanceledException"/>。
    /// </summary>
    public async Task EnterAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        await _semaphore.WaitAsync(linked.Token).ConfigureAwait(false);
    }

    /// <summary>退出在途操作（与 <see cref="EnterAsync"/> 配对，含异常路径）。</summary>
    public void Exit()
    {
        try
        {
            _semaphore.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// 取消生命周期并有界等待在途操作退出。返回 true = 已空闲（可安全销毁对象）；
    /// false = 超时（在途操作仍未退出）。返回前释放信号量，保持门可复用（见 <see cref="Reset"/>）。
    /// </summary>
    public bool CancelAndWait(TimeSpan timeout)
    {
        try
        {
            _lifetime.Cancel();
        }
        catch
        {
        }

        if (Volatile.Read(ref _disposed) != 0) return true;

        var acquired = false;
        try
        {
            acquired = _semaphore.Wait(timeout);
            return acquired;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
        finally
        {
            if (acquired)
            {
                try
                {
                    _semaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    /// <summary>重新武装生命周期（仅应在 <see cref="CancelAndWait"/> 返回 true、确认空闲后调用）。</summary>
    public void Reset()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (!_lifetime.IsCancellationRequested) return;

        var old = _lifetime;
        _lifetime = new CancellationTokenSource();
        try
        {
            old.Dispose();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        try
        {
            _lifetime.Cancel();
        }
        catch
        {
        }

        try
        {
            _lifetime.Dispose();
        }
        catch
        {
        }

        try
        {
            _semaphore.Dispose();
        }
        catch
        {
        }
    }
}
