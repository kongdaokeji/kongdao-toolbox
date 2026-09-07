using Microsoft.UI.Xaml;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>
/// 「新手教程」独立窗口宿主（不注册进 BuiltinToolRegistry，仅由主界面标题栏按钮打开）。
/// 样式与其它内置工具窗口一致：Mica 背景、82%×85% 工作区、Tall 扩展标题栏，主题实时跟随。
/// </summary>
public static class NewbieTutorialWindow
{
    private static Window? _window;

    public static void Show()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        var window = new Window();
        var page = new NewbieTutorialPage();
        page.RequestedTheme = ThemeService.CurrentElementTheme;
        window.Content = page;

        BuiltinWindowHelper.ApplyStandardStyle(window, "新手教程");

        void OnThemeChanged(ElementTheme theme)
        {
            page.RequestedTheme = theme;
            BuiltinWindowHelper.ApplyTitleBarTheme(window);
        }
        ThemeService.ThemeChanged += OnThemeChanged;
        window.Closed += (_, _) =>
        {
            ThemeService.ThemeChanged -= OnThemeChanged;
            _window = null;
        };

        _window = window;
        window.Activate();
    }
}
