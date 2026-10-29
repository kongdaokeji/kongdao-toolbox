using Microsoft.Web.WebView2.Core;

namespace TubaWinUi3.Services;

/// <summary>
/// 共享的 WebView2 环境：用户数据目录固定在 %LocalAppData%\TubaWinUi3\WebView2，
/// 避免应用安装在不可写目录（如 Program Files、MSIX 的 WindowsApps）时，
/// WebView2 无法在 exe 旁创建默认的 *.exe.WebView2 数据目录而报错。
/// 目录由此处显式指定，不经 WEBVIEW2_USER_DATA_FOLDER 环境变量——后者会连带覆盖
/// 工具箱 WebView2 环境的用户数据目录（详见 App() 中的说明）。
/// </summary>
public static class WebView2EnvironmentService
{
    /// <summary>共享的 WebView2 用户数据目录（缓存所在位置）。</summary>
    public static string UserDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TubaWinUi3", "WebView2");

    private static readonly Lazy<Task<CoreWebView2Environment>> _environment = new(
        () => CoreWebView2Environment.CreateWithOptionsAsync(
            browserExecutableFolder: null,
            userDataFolder: UserDataFolder,
            options: new CoreWebView2EnvironmentOptions()).AsTask());

    /// <summary>获取共享环境（所有 WebView2 实例共用同一用户数据目录与浏览器进程）。</summary>
    public static Task<CoreWebView2Environment> GetAsync() => _environment.Value;
}
