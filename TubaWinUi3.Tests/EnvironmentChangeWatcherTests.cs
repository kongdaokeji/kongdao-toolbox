using System.Runtime.InteropServices;
using TubaWinUi3.Services.EnvVars;

namespace TubaWinUi3.Tests;

/// <summary>
/// 窗口子类化监听的生命周期：装上、装不上不登记、重复调用不重复登记、卸载恢复原过程、
/// 窗口销毁自动摘除登记（否则静态字典泄漏，失效句柄上还会继续走 CallWindowProc）。
/// 用真实窗口验证：CreateWindowExW 建一个 message-only 窗口——不需要消息泵，
/// SendMessage 同线程同步直达窗口过程。
/// </summary>
public class EnvironmentChangeWatcherTests
{
    private static readonly IntPtr HwndMessage = new(-3);
    private const int GWLP_WNDPROC = -4;
    private const uint WM_SETTINGCHANGE = 0x001A;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongW(IntPtr hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static IntPtr GetWindowProc(IntPtr hwnd)
        => IntPtr.Size == 8 ? GetWindowLongPtrW(hwnd, GWLP_WNDPROC) : GetWindowLongW(hwnd, GWLP_WNDPROC);

    private static IntPtr CreateTestWindow()
    {
        // 用系统内置的 Static 类，省去注册窗口类；样式 0，挂在 HWND_MESSAGE 下
        var hwnd = CreateWindowExW(
            0, "Static", "tuba-envvar-watcher-test", 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, hwnd);
        return hwnd;
    }

    [Fact]
    public void EnsureInstalled_InstallsHookAndIsIdempotent()
    {
        var hwnd = CreateTestWindow();
        var originalProc = GetWindowProc(hwnd);
        var countBefore = EnvironmentChangeWatcher.HookCount;

        try
        {
            Assert.True(EnvironmentChangeWatcher.EnsureInstalled(hwnd));
            Assert.True(EnvironmentChangeWatcher.IsInstalled(hwnd));
            Assert.Equal(countBefore + 1, EnvironmentChangeWatcher.HookCount);
            Assert.NotEqual(originalProc, GetWindowProc(hwnd));   // 窗口过程确实换成了我们的

            // 重复安装：幂等，不产生第二个钩子
            Assert.False(EnvironmentChangeWatcher.EnsureInstalled(hwnd));
            Assert.Equal(countBefore + 1, EnvironmentChangeWatcher.HookCount);
        }
        finally
        {
            DestroyWindow(hwnd);
        }
    }

    [Fact]
    public void EnsureInstalled_OnInvalidWindow_DoesNotRegisterHook()
    {
        var countBefore = EnvironmentChangeWatcher.HookCount;

        // 无效句柄：SetWindowLongPtrW 只会返回 0（不抛异常），绝不能把 0 当旧过程登记下来。
        // 用奇数：句柄按 4 字节对齐，奇数句柄永远不可能是真的
        Assert.False(EnvironmentChangeWatcher.EnsureInstalled(new IntPtr(0x1235)));
        Assert.Equal(countBefore, EnvironmentChangeWatcher.HookCount);

        // 0 句柄直接拒绝
        Assert.False(EnvironmentChangeWatcher.EnsureInstalled(IntPtr.Zero));
        Assert.Equal(countBefore, EnvironmentChangeWatcher.HookCount);
    }

    [Fact]
    public void Uninstall_RestoresOriginalProcAndRemovesHook()
    {
        var hwnd = CreateTestWindow();
        var originalProc = GetWindowProc(hwnd);
        var countBefore = EnvironmentChangeWatcher.HookCount;

        try
        {
            Assert.True(EnvironmentChangeWatcher.EnsureInstalled(hwnd));
            Assert.True(EnvironmentChangeWatcher.Uninstall(hwnd));

            Assert.False(EnvironmentChangeWatcher.IsInstalled(hwnd));
            Assert.Equal(countBefore, EnvironmentChangeWatcher.HookCount);
            Assert.Equal(originalProc, GetWindowProc(hwnd));   // 原窗口过程原样还原

            // 卸载后再卸一次：幂等，不抛异常
            Assert.False(EnvironmentChangeWatcher.Uninstall(hwnd));
        }
        finally
        {
            DestroyWindow(hwnd);
        }
    }

    [Fact]
    public void WindowDestroyed_AutoRemovesHook()
    {
        var hwnd = CreateTestWindow();
        var countBefore = EnvironmentChangeWatcher.HookCount;

        Assert.True(EnvironmentChangeWatcher.EnsureInstalled(hwnd));
        Assert.Equal(countBefore + 1, EnvironmentChangeWatcher.HookCount);

        Assert.True(DestroyWindow(hwnd));   // 销毁时窗口过程收到 WM_NCDESTROY，登记应当被摘掉

        Assert.False(EnvironmentChangeWatcher.IsInstalled(hwnd));
        Assert.Equal(countBefore, EnvironmentChangeWatcher.HookCount);   // 静态字典不泄漏
    }

    [Fact]
    public void ExternalSettingChangeBroadcast_RaisesEvent_OwnMarkerAndOtherAreasDoNot()
    {
        var hwnd = CreateTestWindow();
        var raised = 0;
        Action handler = () => raised++;

        try
        {
            Assert.True(EnvironmentChangeWatcher.EnsureInstalled(hwnd));
            EnvironmentChangeWatcher.EnvironmentChangedExternally += handler;

            var environment = Marshal.StringToHGlobalUni("Environment");
            try
            {
                // 外部程序（wParam = 0）广播：应当触发
                SendMessageW(hwnd, WM_SETTINGCHANGE, IntPtr.Zero, environment);
                Assert.Equal(1, raised);

                // 本程序自己广播（自定义 wParam）：绝不允自触发
                SendMessageW(hwnd, WM_SETTINGCHANGE, (IntPtr)EnvironmentVariableService.OwnBroadcastMarker, environment);
                Assert.Equal(1, raised);

                // 别的通知域（lParam 不是 "Environment"）：不触发
                var otherArea = Marshal.StringToHGlobalUni("WindowMetrics");
                try
                {
                    SendMessageW(hwnd, WM_SETTINGCHANGE, IntPtr.Zero, otherArea);
                    Assert.Equal(1, raised);
                }
                finally
                {
                    Marshal.FreeHGlobal(otherArea);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(environment);
            }
        }
        finally
        {
            EnvironmentChangeWatcher.EnvironmentChangedExternally -= handler;
            DestroyWindow(hwnd);
        }
    }
}
