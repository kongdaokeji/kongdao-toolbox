using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

/// <summary>
/// 统一内置工具窗口样式：与"电脑使用教程"一致 —— Mica 背景、82%×85% 工作区尺寸并居中、
/// 扩展标题栏（Tall）+ 自定义主题配色。
/// </summary>
public static class BuiltinWindowHelper
{
    public static void ApplyStandardStyle(Window window, string title)
    {
        BackdropService.ApplyBackdrop(window);
        window.AppWindow.Title = title;

        try
        {
            var displayArea = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea is not null)
            {
                var workArea = displayArea.WorkArea;
                var w = (int)(workArea.Width * 0.82);
                var h = (int)(workArea.Height * 0.85);
                window.AppWindow.Resize(new SizeInt32(w, h));
                window.AppWindow.Move(new PointInt32(
                    workArea.X + (int)((workArea.Width - w) / 2),
                    workArea.Y + (int)((workArea.Height - h) / 2)));
            }
        }
        catch
        {
            window.AppWindow.Resize(new SizeInt32(1100, 750));
            try
            {
                var mainPos = App.MainWindow?.AppWindow.Position;
                if (mainPos is not null)
                    window.AppWindow.Move(new PointInt32(mainPos.Value.X + 50, mainPos.Value.Y + 50));
            }
            catch { }
        }

        window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        ApplyTitleBarTheme(window);
    }

    public static void ApplyTitleBarTheme(Window window)
    {
        var isDark = ThemeService.CurrentTheme == AppTheme.Dark ||
                     (ThemeService.CurrentTheme == AppTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
        TitleBarPalette.Apply(window.AppWindow.TitleBar, isDark);
    }
}

/// <summary>
/// 全应用统一的标题栏配色（主窗口与所有子窗口共用同一套明暗色值）。
/// </summary>
internal static class TitleBarPalette
{
    public static void Apply(AppWindowTitleBar tb, bool isDark)
    {
        if (isDark)
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 180, 180, 180);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.BackgroundColor = Color.FromArgb(255, 32, 32, 32);
            tb.InactiveBackgroundColor = Color.FromArgb(255, 32, 32, 32);
        }
        else
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);
            tb.BackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.InactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
        }

        tb.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
    }
}
