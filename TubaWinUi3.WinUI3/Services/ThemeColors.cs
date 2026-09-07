using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Services;

/// <summary>
/// 主题感知颜色，供代码构建 UI 的内置工具使用。
/// 值直接取自 WinUI 官方主题资源（自动适配明暗/高对比），不再维护独立调色板。
/// 回退值仅在资源缺失（如单元测试宿主）时使用，取亮色默认。
/// </summary>
internal static class ThemeColors
{
    private static Color GetColor(string key, Color fallback)
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

    public static Color CardBg => GetColor("CardBackgroundFillColorDefaultBrush", Color.FromArgb(255, 249, 249, 249));
    public static Color BorderColor => GetColor("CardStrokeColorDefaultBrush", Color.FromArgb(255, 229, 229, 229));
    public static Color DimText => GetColor("TextFillColorTertiaryBrush", Color.FromArgb(255, 110, 110, 110));
    public static Color SecondaryText => GetColor("TextFillColorSecondaryBrush", Color.FromArgb(255, 70, 70, 70));
    public static Color PrimaryText => GetColor("TextFillColorPrimaryBrush", Color.FromArgb(255, 30, 30, 30));
    public static Color HeaderBg => GetColor("SubtleFillColorSecondaryBrush", Color.FromArgb(255, 245, 245, 245));
    public static Color RowHover => GetColor("SubtleFillColorTertiaryBrush", Color.FromArgb(255, 240, 240, 240));
    public static Color DisabledBg => GetColor("ControlFillColorDisabledBrush", Color.FromArgb(255, 240, 240, 240));
    public static Color KeyDefault => GetColor("ControlFillColorDefaultBrush", Color.FromArgb(255, 230, 230, 230));
    public static Color KeyBorder => GetColor("ControlStrokeColorDefaultBrush", Color.FromArgb(255, 200, 200, 200));
    public static Color KeyText => GetColor("TextFillColorPrimaryBrush", Color.FromArgb(255, 30, 30, 30));
    public static Color KeyboardBg => GetColor("LayerFillColorDefaultBrush", Color.FromArgb(255, 240, 240, 240));
    public static Color SubtleBg => GetColor("SubtleFillColorSecondaryBrush", Color.FromArgb(255, 240, 240, 240));
    public static Color SubtleBgHover => GetColor("SubtleFillColorTertiaryBrush", Color.FromArgb(255, 230, 230, 230));
    public static Color Separator => GetColor("DividerStrokeColorDefaultBrush", Color.FromArgb(255, 220, 220, 220));

    // 语义强调色：跟随系统强调色与语义色，替代此前的固定 Tailwind 调色板
    public static Color AccentBlue => GetColor("SystemAccentColor", Color.FromArgb(255, 96, 165, 250));
    public static Color AccentGreen => GetColor("SystemFillColorSuccessBrush", Color.FromArgb(255, 74, 222, 128));
    public static Color AccentOrange => GetColor("SystemFillColorCautionBrush", Color.FromArgb(255, 251, 191, 36));
    public static Color AccentRed => GetColor("SystemFillColorCriticalBrush", Color.FromArgb(255, 248, 113, 113));

    // 无官方语义等价物的品牌色，保留固定值
    public static readonly Color AccentPurple = Color.FromArgb(255, 167, 139, 250);
    public static readonly Color DodgerBlue = Color.FromArgb(255, 30, 144, 255);
}
