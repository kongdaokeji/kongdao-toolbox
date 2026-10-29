using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Services;

/// <summary>
/// ContentDialog 并发守卫：WinUI 同一时刻只允许打开一个 ContentDialog，并发 ShowAsync
/// 会抛 COMException「Only a single ContentDialog can be open at any time.」。
/// 后台/静默流程（内核更新提示、下载完成提示）弹窗前必须先等已打开的对话框关闭，
/// 否则异常会从未处理的 async 回调逃逸，直接进全局崩溃上报。
/// </summary>
internal static class ContentDialogGuard
{
    // 串行化守卫自身的弹窗：等待期间互斥，避免两个等待方同时判定「空闲」后抢占。
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private const int PollIntervalMs = 100;
    private const int MaxPopupScanDepth = 4;

    /// <summary>指定 XamlRoot 上是否还有未关闭的 ContentDialog（含其他页面/流程打开的）。</summary>
    public static bool IsAnyOpen(XamlRoot? xamlRoot)
    {
        if (xamlRoot is null) return false;

        try
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
            {
                if (ContainsContentDialog(popup.Child, 0)) return true;
            }
        }
        catch
        {
            // 探测失败时按「无对话框」处理：宁可尝试显示，也不因探测异常卡死调用方。
        }
        return false;
    }

    /// <summary>
    /// 弹窗根节点通常就是 ContentDialog 本身；保留有界子树扫描，兜住框架包装出中间节点的情况。
    /// </summary>
    private static bool ContainsContentDialog(DependencyObject? node, int depth)
    {
        if (node is null || depth > MaxPopupScanDepth) return false;
        if (node is ContentDialog) return true;

        try
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                if (ContainsContentDialog(VisualTreeHelper.GetChild(node, i), depth + 1)) return true;
            }
        }
        catch
        {
        }
        return false;
    }

    /// <summary>
    /// 等已有对话框关闭后再显示 <paramref name="dialog"/>；超时或显示失败返回 false，不抛异常。
    /// 调用方需已设置 dialog.XamlRoot（构造后、展示前设置即可）。
    /// </summary>
    public static async Task<bool> ShowWhenIdleAsync(ContentDialog dialog, TimeSpan timeout)
    {
        if (dialog.XamlRoot is null) return false;

        var deadline = DateTime.UtcNow + timeout;
        if (!await WaitAsync(_gate, deadline).ConfigureAwait(true)) return false;

        try
        {
            while (IsAnyOpen(dialog.XamlRoot))
            {
                if (DateTime.UtcNow >= deadline) return false;
                await Task.Delay(PollIntervalMs).ConfigureAwait(true);
            }

            await dialog.ShowAsync();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<bool> WaitAsync(SemaphoreSlim gate, DateTime deadline)
    {
        var remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) return false;

        try { return await gate.WaitAsync(remaining).ConfigureAwait(true); }
        catch { return false; }
    }
}
