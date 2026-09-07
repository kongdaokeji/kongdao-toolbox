namespace TubaWinUi3.Services;

public sealed class ServiceCenterTool : IBuiltinTool
{
    public string Id => "service-center";
    public string Name => "服务网点查询";
    public string Description => "查询各大品牌笔记本、台式机官方服务网点地址。";
    public string Glyph => "\uE80F";
    public string Category => "实用工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.ServiceCenterPage));
        return Task.CompletedTask;
    }
}