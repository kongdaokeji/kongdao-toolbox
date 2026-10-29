using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.EnvVars;

/// <summary>
/// 监听外部程序广播的 WM_SETTINGCHANGE（环境变量变化），用于提示用户「别处改了环境变量」。
///
/// WinUI 3 不暴露托管 WndProc，只能子类化主窗口（与 <see cref="Win32DropHelper"/> 同一套做法）。
/// 硬要求，错一条不是崩溃就是吃掉别人的钩子：
/// 1. 委托必须存进静态字典防 GC——被回收后窗口过程就是野指针；
/// 2. 其余消息必须用 <c>CallWindowProcW(旧过程, …)</c> 链式转发；
/// 3. <c>lParam</c> 是指向宽字符串的指针，<b>只能在 WndProc 内同步解读</b>——
///    SendMessageTimeout 返回后那块内存就失效了；
/// 4. 幂等安装，失败不能影响启动，且<b>装不上时绝不登记无效钩子</b>：
///    SetWindowLongPtrW 失败只返回 0（不抛异常），把它当旧过程记下来，之后 CallWindowProcW(0, …) 就是未定义行为；
/// 5. 窗口销毁（WM_NCDESTROY）时自动摘除登记：不留失效句柄的钩子，静态字典也不泄漏。
/// </summary>
public static class EnvironmentChangeWatcher
{
    /// <summary>检测到外部修改环境变量时触发（WndProc 线程；订阅者自行调度到 UI 线程）。</summary>
    public static event Action? EnvironmentChangedExternally;

    private sealed class WindowHook
    {
        public IntPtr OldWndProc;
        public WndProcDelegate Delegate = null!;   // 持有引用防止被 GC 回收
    }

    private static readonly Dictionary<IntPtr, WindowHook> Hooks = new();

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, WndProcDelegate newProc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetWindowLongW(IntPtr hWnd, int nIndex, WndProcDelegate newProc);

    // 还原旧过程要传裸函数指针，不能复用上面的委托重载
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW_IntPtr(IntPtr hWnd, int nIndex, IntPtr newProc);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetWindowLongW_IntPtr(IntPtr hWnd, int nIndex, IntPtr newProc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int GWLP_WNDPROC = -4;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint WM_NCDESTROY = 0x0082;
    private const string EnvironmentArea = "Environment";

    /// <summary>测试用：当前登记在册的钩子数量（断言「不泄漏 / 不重复登记」）。</summary>
    internal static int HookCount => Hooks.Count;

    /// <summary>测试用：某个窗口是否已挂上钩子。</summary>
    internal static bool IsInstalled(IntPtr hwnd) => Hooks.ContainsKey(hwnd);

    /// <summary>
    /// 对窗口安装监听（幂等：同一窗口只装一次）。装上了返回 true。
    /// SetWindowLongPtrW 失败（无效句柄等）时只记诊断、<b>不登记钩子</b>，
    /// 绝不让字典里出现 OldWndProc = 0 的条目（那会让后续消息走进 CallWindowProcW(0, …)）。
    /// </summary>
    public static bool EnsureInstalled(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || Hooks.ContainsKey(hwnd)) return false;

        var hook = new WindowHook { Delegate = WndProcSubclass };

        // SetLastError 约定：先清零再判断。GWLP_WNDPROC 的旧过程不可能是 0（那意味着窗口没有窗口过程），
        // 所以「返回 0」一律按失败处理——宁可少一个提示，也不冒 CallWindowProcW(0) 的风险。
        Marshal.SetLastPInvokeError(0);
        hook.OldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, hook.Delegate);

        if (hook.OldWndProc == IntPtr.Zero)
        {
            Debug.WriteLine(
                $"[EnvVars] 窗口子类化失败，未安装环境变量变更监听（hwnd=0x{hwnd.ToInt64():X}，错误码 {Marshal.GetLastPInvokeError()}）");
            return false;
        }

        Hooks[hwnd] = hook;
        return true;
    }

    /// <summary>
    /// 摘除监听、恢复原窗口过程并移出静态字典（窗口还活着时用）。
    /// 窗口销毁不需要调用——WM_NCDESTROY 会清掉登记。
    /// 返回 false = 本来就没装；装了但还原失败（句柄已失效等）同样返回 false（登记已移除，诊断在 Debug 输出）。
    /// 注意只按「后装先卸」顺序安全：本程序里没有生产调用方，只有测试用。
    /// </summary>
    public static bool Uninstall(IntPtr hwnd)
    {
        if (!Hooks.Remove(hwnd, out var hook)) return false;

        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(hwnd, GWLP_WNDPROC, hook.OldWndProc);
        if (previous == IntPtr.Zero)
        {
            Debug.WriteLine(
                $"[EnvVars] 还原窗口过程失败（hwnd=0x{hwnd.ToInt64():X}，错误码 {Marshal.GetLastPInvokeError()}）");
            return false;
        }

        return true;
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate newProc)
        => IntPtr.Size == 8
            ? SetWindowLongPtrW(hWnd, nIndex, newProc)
            : SetWindowLongW(hWnd, nIndex, newProc);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newProc)
        => IntPtr.Size == 8
            ? SetWindowLongPtrW_IntPtr(hWnd, nIndex, newProc)
            : SetWindowLongW_IntPtr(hWnd, nIndex, newProc);

    private static IntPtr WndProcSubclass(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // 窗口正在销毁：摘掉登记并把最后这条消息链给原过程（之后不会再有消息到这个 hwnd）。
        // 销毁流程里不再调 SetWindowLongPtrW：窗口正在消失，只清理登记即可。
        if (msg == WM_NCDESTROY)
        {
            var oldProc = Hooks.TryGetValue(hWnd, out var dying) ? dying.OldWndProc : IntPtr.Zero;
            Hooks.Remove(hWnd);

            return oldProc != IntPtr.Zero
                ? CallWindowProcW(oldProc, hWnd, msg, wParam, lParam)
                : DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        // wParam == 0：只有外部程序（系统属性对话框等）是这么发的；
        // 本程序自己广播时 wParam = EnvironmentVariableService.OwnBroadcastMarker，所以不会自触发。
        if (msg == WM_SETTINGCHANGE && wParam == IntPtr.Zero && lParam != IntPtr.Zero
            && Marshal.PtrToStringUni(lParam) == EnvironmentArea)
        {
            try
            {
                EnvironmentChangedExternally?.Invoke();
            }
            catch
            {
                // 订阅者抛异常绝不能穿出 WndProc
            }
        }

        if (Hooks.TryGetValue(hWnd, out var hook))
            return CallWindowProcW(hook.OldWndProc, hWnd, msg, wParam, lParam);

        // 兜底：SetWindowLongPtr 装好过程与写入字典之间若恰有消息进来会走到这里。
        // 这是「本来就不该发生」的分支，用 DefWindowProc 是为了绝不让 WndProc 返回垃圾值。
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }
}
