using Microsoft.UI.Xaml;

namespace TubaWinUi3.Services;

public sealed class WindowsImageTool : IBuiltinTool
{
    public string Id => "windows-image";
    public string Name => "Windows 镜像";
    public string Description => "下载 Windows 原版系统镜像（ISO/ESD），支持 ESD 转 ISO。";
    public string Glyph => "\uE896";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.WindowsImagePage));
        return Task.CompletedTask;
    }
}
