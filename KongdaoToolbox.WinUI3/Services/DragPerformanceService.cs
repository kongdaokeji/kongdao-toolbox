using System.Runtime.InteropServices;

namespace KongdaoToolbox.Services;

/// <summary>
/// 拖动/缩放期间的「轻量模式」。
///
/// <para>
/// WinUI 3 在 Windows 10 上不是 DWM 原生合成窗口：每次位置变化都要重新提交整棵可视化树。
/// 高刷新率显示器 + 高回报率鼠标时，移动消息的产生速率会超过单帧合成能力，
/// 消息在队列里堆积 —— 表现出来不是「帧率低」，而是**拖影、跟随延迟、鼠标停下后窗口还在继续动**。
/// </para>
/// <para>
/// 这里在 <c>WM_ENTERSIZEMOVE</c> 的第一时间把最贵的合成层卸掉（大尺寸半透明背景图等），
/// <c>WM_EXITSIZEMOVE</c> 恢复。卸掉之后单次合成成本大幅下降，输入不再积压。
/// </para>
/// </summary>
public static class DragPerformanceService
{
    /// <summary>拖动开始/结束（WndProc 线程触发；订阅者必须自行调度到 UI 线程）。</summary>
    public static event Action<bool>? DragStateChanged;

    /// <summary>
    /// 按下标题栏或窗口边框时询问托管侧是否接管。返回 true 表示已接管，
    /// 本服务不再把消息转发给 WinUI，系统不会进入内置的 modal 移动/缩放循环。
    /// 参数：窗口句柄、命中测试码（HTCAPTION=2，HTLEFT..HTBOTTOMRIGHT=10..17）。
    /// </summary>
    public static Func<IntPtr, int, bool>? CustomDragRequested;

    private sealed class WindowHook
    {
        public IntPtr OldWndProc;
        public WndProcDelegate Delegate = null!; // 持有引用，防止被 GC 回收后回调到野指针
    }

    private static readonly Dictionary<IntPtr, WindowHook> _hooks = [];
    private static readonly HashSet<IntPtr> _dragging = [];

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, WndProcDelegate newProc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowLongW(IntPtr hWnd, int nIndex, WndProcDelegate newProc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int GWLP_WNDPROC = -4;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const uint WM_ENTERSIZEMOVE = 0x0231;
    private const uint WM_EXITSIZEMOVE = 0x0232;
    private const uint WM_CAPTURECHANGED = 0x0215;
    private const int HTCAPTION = 2;

    /// <summary>给窗口装上拖动状态钩子（幂等）。与 Win32DropHelper 的子类化可以链式共存。</summary>
    public static void Attach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _hooks.ContainsKey(hwnd)) return;
        var hook = new WindowHook { Delegate = WndProcSubclass };
        hook.OldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, hook.Delegate);
        if (hook.OldWndProc != IntPtr.Zero)
            _hooks[hwnd] = hook;
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate newProc)
        => IntPtr.Size == 8
            ? SetWindowLongPtrW(hWnd, nIndex, newProc)
            : SetWindowLongW(hWnd, nIndex, newProc);

    private static IntPtr WndProcSubclass(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_NCLBUTTONDOWN)
        {
            // 标题栏（HTCAPTION）和各边框（HTLEFT..HTBOTTOMRIGHT）都先问托管侧。
            // 接管时直接吞掉这条消息：系统拿不到 WM_NCLBUTTONDOWN，就不会进入
            // DefWindowProc 内置的移动/缩放循环（那个循环每次鼠标移动都强制整窗重绘，
            // 在 WinUI 3 的合成窗口上代价极高）。托管侧改用节流后的 SetWindowPos 自己算。
            bool handled = false;
            int hit = wParam.ToInt32();
            try { handled = CustomDragRequested?.Invoke(hWnd, hit) == true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[DragPerf] 接管询问失败: {ex.Message}"); }
            if (handled) return IntPtr.Zero;

            if (_dragging.Add(hWnd)) Raise(true);
        }
        else if (msg == WM_ENTERSIZEMOVE)
        {
            if (_dragging.Add(hWnd)) Raise(true);
        }
        else if (msg == WM_EXITSIZEMOVE || msg == WM_CAPTURECHANGED)
        {
            // 只点标题栏不拖动时不会发 WM_EXITSIZEMOVE，靠 WM_CAPTURECHANGED 兜底，
            // 否则内容会一直停在被卸掉的状态。
            if (_dragging.Remove(hWnd)) Raise(false);
        }

        return _hooks.TryGetValue(hWnd, out var hook)
            ? CallWindowProcW(hook.OldWndProc, hWnd, msg, wParam, lParam)
            : DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void Raise(bool dragging)
    {
        try { DragStateChanged?.Invoke(dragging); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[DragPerf] 状态回调失败（已忽略）: {ex.Message}"); }
    }
}
