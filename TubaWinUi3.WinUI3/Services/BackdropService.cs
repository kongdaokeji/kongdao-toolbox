using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using WinRT;

namespace TubaWinUi3.Services;

public enum BackdropType
{
    Mica,
    MicaAlt,
    Acrylic,
    AcrylicThin
}

/// <summary>
/// 窗口背景材质服务。
/// 使用合成控制器(MicaController / DesktopAcrylicController)直接控制材质,
/// 从而支持官方 XAML 背景类(MicaBackdrop / DesktopAcrylicBackdrop)不具备的
/// TintColor / TintOpacity / LuminosityOpacity / FallbackColor 自定义,
/// 以及材质变体(如细亚克力 DesktopAcrylicKind.Thin)。
///
/// 注意(来自官方文档):一旦自定义了上述属性,控制器将不再自动应用系统深浅主题的
/// 默认值。因此本服务在未启用自定义时完全不设置这些属性(系统默认并自动跟随主题),
/// 启用自定义后使用用户设置的全局单色(深浅主题下保持一致)。
/// </summary>
public static class BackdropService
{
    public static event Action? BackdropChanged;

    private const string KeyType = "BackdropType";
    private const string KeyUseCustomTint = "BackdropUseCustomTint";
    private const string KeyTintColor = "BackdropTintColor";
    private const string KeyTintOpacity = "BackdropTintOpacity";
    private const string KeyLuminosityOpacity = "BackdropLuminosityOpacity";

    /// <summary>
    /// 单个窗口的材质上下文:持有控制器、策略配置与事件订阅,
    /// 关闭窗口时释放控制器,避免其继续引用已销毁的窗口。
    /// </summary>
    private sealed class BackdropContext : IDisposable
    {
        public required Window Window { get; init; }
        public required ISystemBackdropController Controller { get; init; }
        public required SystemBackdropConfiguration Configuration { get; init; }
        public FrameworkElement? Root { get; init; }
        public bool IsDisposed { get; private set; }

        public void OnActivated(object sender, WindowActivatedEventArgs args)
        {
            if (IsDisposed) return;
            Configuration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }

        public void OnThemeChanged(FrameworkElement sender, object args)
        {
            if (IsDisposed) return;
            Configuration.Theme = (SystemBackdropTheme)sender.ActualTheme;
        }

        public void OnClosed(object sender, WindowEventArgs args)
        {
            if (IsDisposed) return;
            _contexts.Remove(Window);
            Dispose();
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Window.Activated -= OnActivated;
            Window.Closed -= OnClosed;
            if (Root is not null)
                Root.ActualThemeChanged -= OnThemeChanged;
            Controller.Dispose();
        }
    }

    private static readonly Dictionary<Window, BackdropContext> _contexts = [];

    public static BackdropType GetBackdropType()
    {
        var val = AppSettings.Get(KeyType);
        return Enum.TryParse<BackdropType>(val, out var t) ? t : BackdropType.Mica;
    }

    public static void SetBackdropType(BackdropType type)
    {
        AppSettings.Set(KeyType, type.ToString());
        BackdropChanged?.Invoke();
    }

    public static BackdropCustomization GetCustomization()
    {
        var useCustom = AppSettings.GetBool(KeyUseCustomTint);
        var tint = BackdropSettings.ParseColor(AppSettings.Get(KeyTintColor), BackdropSettings.DefaultTintColor);
        var tintOpacity = BackdropSettings.Clamp01(AppSettings.GetDouble(KeyTintOpacity, BackdropSettings.DefaultTintOpacity));
        var luminosityOpacity = BackdropSettings.Clamp01(AppSettings.GetDouble(KeyLuminosityOpacity, BackdropSettings.DefaultLuminosityOpacity));
        return new BackdropCustomization(useCustom, tint, tintOpacity, luminosityOpacity);
    }

    public static void SetCustomization(BackdropCustomization customization)
    {
        var wasCustom = GetCustomization().UseCustomTint;
        AppSettings.Set(KeyUseCustomTint, customization.UseCustomTint);
        AppSettings.Set(KeyTintColor, BackdropSettings.FormatColor(customization.TintColor));
        AppSettings.Set(KeyTintOpacity, customization.TintOpacity);
        AppSettings.Set(KeyLuminosityOpacity, customization.LuminosityOpacity);

        if (wasCustom && customization.UseCustomTint)
        {
            // 自定义模式下微调数值:原地更新控制器属性,无需重建(实时、平滑)
            foreach (var context in _contexts.Values.ToList())
            {
                if (context.IsDisposed) continue;
                ApplyCustomization(context.Controller, customization);
            }
        }
        else
        {
            // 开关自定义(控制器自定义后无法恢复系统默认,必须重建)
            BackdropChanged?.Invoke();
        }
    }

    /// <summary>将当前设置应用到窗口。材质不受系统支持时自动回退为纯色背景。</summary>
    public static void ApplyBackdrop(Window window)
    {
        // 与控制器方式互斥,清除可能残留的 XAML 背景属性
        if (window.SystemBackdrop is not null)
            window.SystemBackdrop = null;

        RemoveBackdrop(window);

        var type = GetBackdropType();
        var customization = GetCustomization();

        switch (type)
        {
            case BackdropType.Mica:
            case BackdropType.MicaAlt:
                if (!MicaController.IsSupported()) return;
                var mica = new MicaController
                {
                    Kind = type == BackdropType.MicaAlt ? MicaKind.BaseAlt : MicaKind.Base
                };
                if (customization.UseCustomTint)
                    ApplyMicaCustomization(mica, customization);
                AttachBackdrop(window, mica);
                break;

            case BackdropType.Acrylic:
            case BackdropType.AcrylicThin:
                if (!DesktopAcrylicController.IsSupported()) return;
                var acrylic = new DesktopAcrylicController
                {
                    Kind = type == BackdropType.AcrylicThin ? DesktopAcrylicKind.Thin : DesktopAcrylicKind.Base
                };
                if (customization.UseCustomTint)
                    ApplyAcrylicCustomization(acrylic, customization);
                AttachBackdrop(window, acrylic);
                break;
        }
    }

    /// <summary>移除窗口的材质控制器并释放资源。</summary>
    public static void RemoveBackdrop(Window window)
    {
        if (_contexts.TryGetValue(window, out var context))
        {
            _contexts.Remove(window);
            context.Dispose();
        }
    }

    /// <summary>挂载控制器到窗口并登记事件订阅(激活状态、主题、关闭释放)。</summary>
    private static void AttachBackdrop(Window window, ISystemBackdropController controller)
    {
        try
        {
            var root = window.Content as FrameworkElement;
            var configuration = new SystemBackdropConfiguration
            {
                IsInputActive = true,
                Theme = root is not null
                    ? (SystemBackdropTheme)root.ActualTheme
                    : SystemBackdropTheme.Default
            };

            switch (controller)
            {
                case MicaController mica:
                    mica.AddSystemBackdropTarget(window.As<ICompositionSupportsSystemBackdrop>());
                    mica.SetSystemBackdropConfiguration(configuration);
                    break;
                case DesktopAcrylicController acrylic:
                    acrylic.AddSystemBackdropTarget(window.As<ICompositionSupportsSystemBackdrop>());
                    acrylic.SetSystemBackdropConfiguration(configuration);
                    break;
            }

            var context = new BackdropContext
            {
                Window = window,
                Controller = controller,
                Configuration = configuration,
                Root = root
            };

            _contexts[window] = context;
            window.Activated += context.OnActivated;
            window.Closed += context.OnClosed;
            if (root is not null)
                root.ActualThemeChanged += context.OnThemeChanged;
        }
        catch
        {
            // 挂载失败(如合成器不支持)时回退纯色背景
            controller.Dispose();
        }
    }

    private static void ApplyCustomization(ISystemBackdropController controller, BackdropCustomization customization)
    {
        switch (controller)
        {
            case MicaController mica:
                ApplyMicaCustomization(mica, customization);
                break;
            case DesktopAcrylicController acrylic:
                ApplyAcrylicCustomization(acrylic, customization);
                break;
        }
    }

    private static void ApplyMicaCustomization(MicaController controller, BackdropCustomization c)
    {
        controller.TintColor = c.TintColor;
        controller.TintOpacity = (float)c.TintOpacity;
        controller.LuminosityOpacity = (float)c.LuminosityOpacity;
        controller.FallbackColor = c.FallbackColor;
    }

    private static void ApplyAcrylicCustomization(DesktopAcrylicController controller, BackdropCustomization c)
    {
        controller.TintColor = c.TintColor;
        controller.TintOpacity = (float)c.TintOpacity;
        controller.LuminosityOpacity = (float)c.LuminosityOpacity;
        controller.FallbackColor = c.FallbackColor;
    }
}
