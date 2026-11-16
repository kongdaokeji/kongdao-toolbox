using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace KongdaoToolbox.Services;

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

        // 窗口尺寸已定为固定值（1280×800，不允许拖动边框缩放），
        // 不再记忆/恢复上次大小，避免出现「上次被拖成畸形尺寸」的情况。
        ApplyFixedSize(appWindow);
    }

    /// <summary>固定窗口尺寸 1280×800 并在工作区居中；屏幕放不下时按工作区收缩。</summary>
    private static void ApplyFixedSize(AppWindow appWindow)
    {
        const int PreferredWidth = 1280;
        const int PreferredHeight = 800;

        try
        {
            var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea is null)
            {
                appWindow.Resize(new Windows.Graphics.SizeInt32(PreferredWidth, PreferredHeight));
                return;
            }

            var workArea = displayArea.WorkArea;
            int width = Math.Min(PreferredWidth, workArea.Width);
            int height = Math.Min(PreferredHeight, workArea.Height);

            appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
            appWindow.Move(new Windows.Graphics.PointInt32(
                workArea.X + Math.Max(0, (workArea.Width - width) / 2),
                workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowSize] 设置固定窗口尺寸失败（已忽略）: {ex.Message}");
        }
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
