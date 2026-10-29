using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>
/// 「连接手机」内置工具：开启局域网服务，手机端扫码 / 配对码 / IP 连接后可监控、截图、
/// 执行 PowerShell、winget 装软件、互传消息文件。界面在 <see cref="PhoneLinkPage"/>。
/// </summary>
public sealed class PhoneLinkTool : IBuiltinTool
{
    public string Id => "phone-link";
    public string Name => LocalizationService.L("Builtin_phone-link_Name", "连接手机");
    public string Description => LocalizationService.L("Builtin_phone-link_Desc", "手机端通过局域网（二维码 / 配对码 / IP）连接电脑，查看电脑配置与屏幕截图、自选项实时监控（含 FPS 曲线），远程执行 PowerShell、winget 安装软件，并与电脑互传文字、图片和文件。");
    public string Glyph => "\uE8EA";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.BackgroundTask;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(PhoneLinkPage));
        return Task.CompletedTask;
    }
}
