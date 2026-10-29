using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace TubaWinUi3.Services;

public static class WindowSizeService
{
    private const string WidthKey = "WindowWidth";
    private const string HeightKey = "WindowHeight";
    private const string MaximizedKey = "WindowMaximized";
    private const string RememberKey = "RememberWindowSize";

    public static bool IsRememberEnabled()
    {
        return AppSettings.GetBool(RememberKey);
    }

    public static void SetRememberEnabled(bool enabled)
    {
        AppSettings.Set(RememberKey, enabled);
        if (!enabled)
        {
            AppSettings.Remove(WidthKey);
            AppSettings.Remove(HeightKey);
            AppSettings.Remove(MaximizedKey);
        }
    }

    public static void SaveWindowSize(MainWindow window)
    {
        if (!IsRememberEnabled()) return;
        if (window is null) return;

        var appWindow = window.AppWindow;
        if (appWindow is null) return;

        var presenter = appWindow.Presenter as OverlappedPresenter;
        var isMaximized = presenter?.State == OverlappedPresenterState.Maximized;

        if (isMaximized)
        {
            AppSettings.Set(MaximizedKey, true);
        }
        else
        {
            AppSettings.Set(MaximizedKey, false);
            AppSettings.Set(WidthKey, appWindow.Size.Width);
            AppSettings.Set(HeightKey, appWindow.Size.Height);
        }
    }

    public static void ApplySavedWindowSize(MainWindow window)
    {
        if (window is null) return;

        var appWindow = window.AppWindow;

        // 本方法运行在 MainWindow 构造函数里（即 App.OnLaunched 内），
        // 任何一个未捕获异常都会让进程以 STOWED_EXCEPTION 闪退 —— 尺寸还原失败
        // 只应退化成默认尺寸，绝不能拖垮启动。
        if (IsRememberEnabled() && TryRestoreSavedSize(appWindow))
            return;

        ApplyDefaultSize(appWindow);
    }

    private static bool TryRestoreSavedSize(AppWindow appWindow)
    {
        var width = AppSettings.GetInt(WidthKey, 0);
        var height = AppSettings.GetInt(HeightKey, 0);
        if (width <= 0 || height <= 0) return false;

        try
        {
            appWindow.Resize(new Windows.Graphics.SizeInt32(Math.Max(800, width), Math.Max(600, height)));

            if (AppSettings.GetBool(MaximizedKey))
                (appWindow.Presenter as OverlappedPresenter)?.Maximize();

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowSize] 还原上次窗口尺寸失败（改用默认尺寸）: {ex.Message}");
            return false;
        }
    }

    private static void ApplyDefaultSize(AppWindow appWindow)
    {
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea is null) return;

            var workArea = displayArea.WorkArea;
            int width = Math.Max(800, (int)(workArea.Width * 0.8));
            int height = Math.Max(600, (int)(workArea.Height * 0.8));

            appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
            appWindow.Move(new Windows.Graphics.PointInt32(
                workArea.X + (workArea.Width - width) / 2,
                workArea.Y + (workArea.Height - height) / 2));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowSize] 设置默认窗口尺寸失败（已忽略）: {ex.Message}");
        }
    }
}
