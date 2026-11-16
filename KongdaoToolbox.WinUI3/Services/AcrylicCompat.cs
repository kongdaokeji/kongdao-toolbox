using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace KongdaoToolbox.Services;

/// <summary>
/// Windows 10 上的 WinUI Acrylic 是**应用侧**合成：每次窗口位置/尺寸变化都要重新采样窗口
/// 背后的内容并重跑一遍 GPU 高斯模糊。144Hz 拖动时位置每秒变化上百次，模糊也重算上百次，
/// 合成线程被压满 —— 表现正是「静止不卡、一拖就卡」。
///
/// <para>
/// 注意：<see cref="BackdropService"/> 的 Acrylic 守门只管**窗口级** SystemBackdrop，
/// 管不到 XAML 里元素级的 AcrylicBrush（左侧导航 pane 默认背景、首页工具卡片、
/// 搜索弹窗与内容对话框），那几层才是拖动时真正的大面积开销。
/// 本类在 Win10 上把系统 Acrylic 画刷替换为等效的高不透明度纯色：
/// 视觉上仍是接近的深/浅色玻璃感，但不再触发 backdrop 重采样。
/// </para>
/// <para>Windows 11 起 Acrylic 由 DWM 原生合成，代价可接受 —— 该类在 Win11 上完全不介入。</para>
/// </summary>
public static class AcrylicCompat
{
    /// <summary>当前系统是否需要把 Acrylic 降级为纯色（Windows 10，build &lt; 22000）。</summary>
    public static bool UseSolidSurface { get; } = Environment.OSVersion.Version.Build < 22000;

    private static bool _applied;

    /// <summary>
    /// 必须在 <c>App.InitializeComponent()</c> 之后、任何窗口/页面创建之前调用，
    /// 这样所有 <c>ThemeResource Acrylic*</c> 引用第一次解析时就取到纯色画刷。
    ///
    /// <para>
    /// 用带 ThemeDictionaries 的合并字典覆盖，而不是直接往 <c>Application.Resources</c> 写死一个画刷：
    /// 前者由框架在当前主题下取值，浅色/深色自动跟随；后者在用户切主题后会保留旧主题的颜色。
    /// 合并字典排在系统主题字典之前，因此同名 key 会覆盖 WinUI 自带的 AcrylicBrush。
    /// </para>
    /// </summary>
    public static void Apply()
    {
        if (!UseSolidSurface || _applied) return;
        _applied = true;

        var light = new ResourceDictionary();
        FillSolidAcrylic(light, dark: false);
        var dark = new ResourceDictionary();
        FillSolidAcrylic(dark, dark: true);

        var overlay = new ResourceDictionary();
        overlay.ThemeDictionaries["Light"] = light;
        overlay.ThemeDictionaries["Dark"] = dark;
        Application.Current.Resources.MergedDictionaries.Add(overlay);
    }

    private static void FillSolidAcrylic(ResourceDictionary target, bool dark)
    {
        // 与 Fluent 规范里 Acrylic 的 TintColor + TintOpacity 合成结果对齐的中性色。
        var solid = dark ? Color.FromArgb(0xFF, 0x20, 0x20, 0x20) : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3);
        var layer = dark ? Color.FromArgb(0xFF, 0x27, 0x27, 0x27) : Color.FromArgb(0xFF, 0xF9, 0xF9, 0xF9);

        // 元素级 Acrylic 的三组系统键：画刷 + 对应的 Color（少数模板直接取 Color）。
        target["AcrylicInAppFillColorDefaultBrush"] = new SolidColorBrush(solid);
        target["AcrylicInAppFillColorDefault"] = solid;
        target["AcrylicBackgroundFillColorDefaultBrush"] = new SolidColorBrush(layer);
        target["AcrylicBackgroundFillColorDefault"] = layer;
        target["AcrylicBackgroundFillColorBaseBrush"] = new SolidColorBrush(solid);
        target["AcrylicBackgroundFillColorBase"] = solid;
    }

    /// <summary>
    /// 首页工具卡片的等效纯色（原 <c>AcrylicBrush TintColor #FFFFFF/#202020, TintOpacity .45,
    /// TintLuminosityOpacity .35</c>）。保留 0xF2 透明度让观感仍略带通透，但普通 alpha 混合
    /// 的代价可以忽略，不会触发 backdrop 模糊。
    /// </summary>
    public static SolidColorBrush CreateTileSurfaceBrush(ElementTheme theme)
    {
        return theme == ElementTheme.Dark
            ? new SolidColorBrush(Color.FromArgb(0xF2, 0x24, 0x24, 0x24))
            : new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
    }
}
