namespace TubaWinUi3.Services;

public sealed class PortViewerTool : IBuiltinTool
{
    public string Id => "port-viewer";
    public string Name => "端口占用";
    public string Description => "查看系统所有 TCP/UDP 端口占用情况，定位占用进程。";
    public string Glyph => "\uE774";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.PortViewerPage));
        return Task.CompletedTask;
    }
}
