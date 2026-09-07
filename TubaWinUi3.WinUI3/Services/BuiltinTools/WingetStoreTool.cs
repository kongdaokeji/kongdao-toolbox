using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class WingetStoreTool : IBuiltinTool
{
    public string Id => "winget-store";
    public string Name => "正版软件商店";
    public string Description => "浏览并安装正版软件，基于 WinGet 软件源";
    public string Glyph => "\uE719";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(WingetStorePage));
        return Task.CompletedTask;
    }
}
