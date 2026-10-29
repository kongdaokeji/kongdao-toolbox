using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Services;

/// <summary>
/// 内置工具代码构建 UI 的统一工厂，度量全部遵循 Fluent 令牌：
/// 卡片圆角 8 / 内边距 16，图标瓦片 40×40，滚动容器 MaxWidth 1080、Padding 24,0,24,24。
/// 颜色一律取自官方主题资源，禁止硬编码。
/// </summary>
public static class ToolUiHelper
{
    public const double ContentMaxWidth = 1080;
    public static readonly Thickness ScrollPadding = new(24, 0, 24, 24);

    public static Brush GetBrush(string key) => (Brush)Application.Current.Resources[key];

    public static Color GetColor(string key, Color fallback)
    {
        var res = Application.Current.Resources;
        if (res.ContainsKey(key))
        {
            var value = res[key];
            if (value is Color color) return color;
            if (value is SolidColorBrush brush) return brush.Color;
        }
        return fallback;
    }

    /// <summary>统一滚动根：居中、最大宽 1080、标准内边距。</summary>
    public static ScrollViewer CreateScrollRoot(UIElement content, double maxWidth = ContentMaxWidth)
    {
        return new ScrollViewer
        {
            Content = content,
            Padding = ScrollPadding,
            MaxWidth = maxWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled
        };
    }

    /// <summary>
    /// 统一统计卡：图标瓦片（40×40，圆角 8，可带强调色浅底）+ 数值 + Caption 标签。
    /// </summary>
    public static Border MakeStatCard(string label, UIElement value, string glyph, Color? accent = null)
    {
        var tileBackground = accent is Color accentColor
            ? new SolidColorBrush(Color.FromArgb(26, accentColor.R, accentColor.G, accentColor.B))
            : GetBrush("SubtleFillColorSecondaryBrush");

        var iconTile = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(8),
            Background = tileBackground,
            Child = new FontIcon { Glyph = glyph, FontSize = 18 }
        };

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = GetBrush("TextFillColorTertiaryBrush")
        };

        var stack = new StackPanel
        {
            Spacing = 8,
            Children = { iconTile, value, labelBlock }
        };

        return new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = GetBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }

    /// <summary>主操作按钮：统一使用官方 AccentButtonStyle。</summary>
    public static Button MakePrimaryButton(string text)
    {
        var button = new Button { Content = text };
        button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        return button;
    }

    /// <summary>统一状态徽章：药丸圆角、Caption 字号。</summary>
    public static Border MakeBadge(string text, Brush background, Brush foreground)
    {
        return new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(12),
            Background = background,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = foreground
            }
        };
    }
}
