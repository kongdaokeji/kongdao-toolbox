using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Pages;

/// <summary>游戏联机相关弹窗的排版小工具（代码构建 UI 时共用）。</summary>
internal static class GameTunnelUi
{
    public static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
    };

    public static TextBlock Line(string text, double size = 12) => new()
    {
        Text = text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Subtle
    };

    public static TextBlock Emphasis(string text, double size = 13) => new()
    {
        Text = text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap
    };

    public static Border Card(FrameworkElement content, double spacing = 0) => new()
    {
        Padding = new Thickness(14, 12, 14, 12),
        CornerRadius = new CornerRadius(10),
        BorderThickness = new Thickness(1),
        BorderBrush = Border,
        Background = CardBackground,
        Child = content is StackPanel panel ? WithSpacing(panel, spacing) : content
    };

    private static StackPanel WithSpacing(StackPanel panel, double spacing)
    {
        if (spacing > 0) panel.Spacing = spacing;
        return panel;
    }

    public static StackPanel Section(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static Brush Subtle => ThemeBrush("TextFillColorSecondaryBrush");

    public static Brush Border => ThemeBrush("CardStrokeColorDefaultBrush");

    public static Brush CardBackground => ThemeBrush("CardBackgroundFillColorDefaultBrush");

    public static Brush ThemeBrush(string key)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush) return brush;
        }
        catch
        {
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
}
