namespace TubaWinUi3.Services;

public sealed class HostsEditorTool : IBuiltinTool
{
    public string Id => "hosts-editor";
    public string Name => "Hosts 编辑";
    public string Description => "可视化编辑系统 Hosts 文件，支持启用/禁用规则和 DNS 刷新。";
    public string Glyph => "\uE779";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.HostsEditorPage));
        return Task.CompletedTask;
    }
}
