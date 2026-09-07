using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Services;

/// <summary>
/// 空岛品牌强调色单点注入。
/// 在 App 启动期将空岛品牌蓝覆盖进 <see cref="Application.Current.Resources.ThemeDictionaries"/>
/// 的 Light / Dark 主题字典，全站 XAML（{ThemeResource SystemAccentColor*}）与 C#（ThemeColors.*）
/// 读取路径自动跟随，无需逐文件硬编码颜色。
/// </summary>
internal static class BrandAccent
{
    // 空岛品牌蓝（决策 D2 确认值）
    // 主色 #2F79EA / 亮色 #5D9FFF / 深色主题主色 #1B5FE0
    // Light2 / Dark2 为保持对比度的一致派生值。
    private static readonly Color LightAccent = Color.FromArgb(255, 0x2F, 0x79, 0xEA);
    private static readonly Color LightAccentLight1 = Color.FromArgb(255, 0x5D, 0x9F, 0xFF);
    private static readonly Color LightAccentLight2 = Color.FromArgb(255, 0x8F, 0xB8, 0xFF);
    private static readonly Color LightAccentDark1 = Color.FromArgb(255, 0x1B, 0x5F, 0xE0);
    private static readonly Color LightAccentDark2 = Color.FromArgb(255, 0x16, 0x4F, 0xB8);

    private static readonly Color DarkAccent = Color.FromArgb(255, 0x1B, 0x5F, 0xE0);
    private static readonly Color DarkAccentLight1 = Color.FromArgb(255, 0x5D, 0x9F, 0xFF);
    private static readonly Color DarkAccentLight2 = Color.FromArgb(255, 0x8F, 0xB8, 0xFF);
    private static readonly Color DarkAccentDark1 = Color.FromArgb(255, 0x16, 0x4F, 0xB8);
    private static readonly Color DarkAccentDark2 = Color.FromArgb(255, 0x12, 0x3E, 0x8E);

    private static bool _applied;

    // 空岛深空玻璃配色（参考空岛安装器：近黑深空底 #0B0D12 + 玻璃面板 + 蓝辉光描边）
    private static readonly Color SpaceBackground = Color.FromArgb(255, 0x0B, 0x0D, 0x12);
    private static readonly Color GlassCard = Color.FromArgb(255, 0x14, 0x1A, 0x26);
    private static readonly Color GlassCardSecondary = Color.FromArgb(255, 0x10, 0x15, 0x1F);
    private static readonly Color GlassStroke = Color.FromArgb(255, 0x24, 0x2D, 0x3E);
    private static readonly Color SpaceLayer = Color.FromArgb(255, 0x11, 0x16, 0x21);
    private static readonly Color SpaceControl = Color.FromArgb(255, 0x1A, 0x21, 0x2E);

    /// <summary>
    /// 向 Light / Dark 主题字典覆盖 5 个强调色键。需在任意窗口创建前调用（建议 App 构造函数 InitializeComponent 之后）。
    /// 幂等：多次调用仅首次生效。
    /// </summary>
    public static void Apply()
    {
        if (_applied)
            return;

        try
        {
            var resources = Application.Current?.Resources;
            if (resources is null)
                return;

            ApplyToTheme(resources, "Light",
                LightAccent, LightAccentLight1, LightAccentLight2, LightAccentDark1, LightAccentDark2);
            ApplyToTheme(resources, "Dark",
                DarkAccent, DarkAccentLight1, DarkAccentLight2, DarkAccentDark1, DarkAccentDark2);

            ApplyDeepSpaceTheme(resources);

            _applied = true;
        }
        catch
        {
            // 注入失败不应阻断启动；退化为系统强调色。
        }
    }

    /// <summary>
    /// 深空玻璃主题：仅覆盖 Dark 字典的面板/背景/描边刷，让全站页面、卡片、侧栏
    /// 进入空岛安装器同款深空质感（用户切到浅色主题时不受影响）。
    /// </summary>
    private static void ApplyDeepSpaceTheme(ResourceDictionary resources)
    {
        if (!resources.ThemeDictionaries.TryGetValue("Dark", out var themeObj) || themeObj is not ResourceDictionary dark)
        {
            dark = new ResourceDictionary();
            resources.ThemeDictionaries["Dark"] = dark;
        }

        void Set(string key, Color color) => dark[key] = new SolidColorBrush(color);

        Set("ApplicationPageBackgroundThemeBrush", SpaceBackground);
        Set("CardBackgroundFillColorDefaultBrush", GlassCard);
        Set("CardBackgroundFillColorSecondaryBrush", GlassCardSecondary);
        Set("CardStrokeColorDefaultBrush", GlassStroke);
        Set("LayerFillColorDefaultBrush", SpaceLayer);
        Set("LayerFillColorAltBrush", SpaceLayer);
        Set("LayerOnMicaBaseAltFillColorDefaultBrush", SpaceLayer);
        Set("ControlFillColorDefaultBrush", SpaceControl);
        Set("ControlFillColorSecondaryBrush", SpaceControl);
        Set("SolidBackgroundFillColorBaseBrush", SpaceBackground);
        Set("SolidBackgroundFillColorSecondaryBrush", SpaceBackground);
        Set("SolidBackgroundFillColorTertiaryBrush", SpaceBackground);
    }

    private static void ApplyToTheme(ResourceDictionary resources, string key,
        Color accent, Color light1, Color light2, Color dark1, Color dark2)
    {
        if (!resources.ThemeDictionaries.TryGetValue(key, out var themeObj) || themeObj is not ResourceDictionary theme)
        {
            theme = new ResourceDictionary();
            resources.ThemeDictionaries[key] = theme;
        }

        // 使用 SolidColorBrush 以同时兼容 XAML {ThemeResource} 与 C# 端读取 Color（ThemeColors.GetColor 两者皆支持）。
        theme["SystemAccentColor"] = new SolidColorBrush(accent);
        theme["SystemAccentColorLight1"] = new SolidColorBrush(light1);
        theme["SystemAccentColorLight2"] = new SolidColorBrush(light2);
        theme["SystemAccentColorDark1"] = new SolidColorBrush(dark1);
        theme["SystemAccentColorDark2"] = new SolidColorBrush(dark2);
    }
}
