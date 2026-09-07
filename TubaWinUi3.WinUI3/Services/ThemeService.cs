using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Services;

public static class ThemeService
{
    private static AppTheme _currentTheme = AppTheme.Default;

    /// <summary>
    /// 主题应用后触发（参数为解析后的 ElementTheme）。
    /// 子窗口/对话框宿主订阅此事件以实现主题实时跟随。
    /// </summary>
    public static event Action<ElementTheme>? ThemeChanged;

    public static AppTheme CurrentTheme => _currentTheme;

    public static ElementTheme CurrentElementTheme => _currentTheme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    public static void ApplySavedTheme()
    {
        // 空岛品牌：默认深空玻璃主题（对齐空岛安装器观感）。
        // 上游未实现主题持久化，设置页仅展示当前主题，此处安全。
        _currentTheme = AppTheme.Dark;
        ApplyTheme(_currentTheme);
    }

    private static void ApplyTheme(AppTheme theme)
    {
        var window = App.MainWindow;
        if (window?.Content is not FrameworkElement root)
            return;

        var elementTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        root.RequestedTheme = elementTheme;

        if (window is MainWindow mw)
            mw.ApplyTitleBarTheme(elementTheme);

        ThemeChanged?.Invoke(elementTheme);
    }
}

public enum AppTheme
{
    Default,
    Light,
    Dark
}
