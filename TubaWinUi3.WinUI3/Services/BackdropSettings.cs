using Windows.UI;

namespace TubaWinUi3.Services;

/// <summary>
/// 背景材质的自定义参数(全局单色,深浅主题下保持一致)。
/// FallbackColor 不单独保存,由 TintColor 派生(与官方文档示例一致)。
/// </summary>
public sealed record BackdropCustomization(
    bool UseCustomTint,
    Color TintColor,
    double TintOpacity,
    double LuminosityOpacity)
{
    /// <summary>材质无法渲染(节电模式、低端硬件等)时的回退纯色。</summary>
    public Color FallbackColor => BackdropSettings.DeriveFallbackColor(TintColor);
}

/// <summary>
/// 背景材质自定义的纯逻辑部分(颜色解析/格式化、默认值),与 UI 无关,便于单元测试。
/// </summary>
public static class BackdropSettings
{
    /// <summary>默认自定义色调(中性深灰,深浅主题下都可用)。</summary>
    public static readonly Color DefaultTintColor = Color.FromArgb(255, 32, 32, 32);

    /// <summary>默认色调不透明度。</summary>
    public const double DefaultTintOpacity = 0.6;

    /// <summary>默认明度不透明度。</summary>
    public const double DefaultLuminosityOpacity = 0.3;

    public static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    /// <summary>解析 "#RRGGBB" 或 "#AARRGGBB"(# 可省略),失败时返回 fallback。</summary>
    public static Color ParseColor(string? text, Color fallback)
        => TryParseColor(text, out var color) ? color : fallback;

    public static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var hex = text.Trim().TrimStart('#');
        if (hex.Length is not (6 or 8)) return false;
        if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
            return false;

        if (hex.Length == 6)
        {
            color = Color.FromArgb(255,
                (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }
        else
        {
            color = Color.FromArgb(
                (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }
        return true;
    }

    /// <summary>格式化为 "#RRGGBB"(不透明)或 "#AARRGGBB"(带透明度)。</summary>
    public static string FormatColor(Color color)
        => color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>回退色 = 不透明的色调色。</summary>
    public static Color DeriveFallbackColor(Color tint)
        => Color.FromArgb(255, tint.R, tint.G, tint.B);
}
