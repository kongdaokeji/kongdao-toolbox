namespace TubaWinUi3.Services;

public sealed class OfficialWebsitesTool : IBuiltinTool
{
    public string Id => "official-websites";
    public string Name => "常用官网";
    public string Description => "收录 Steam、Epic、UU 加速器等常用软件官方网站，一键直达。";
    public string Glyph => "\uE8F1";
    public string Category => "实用工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.OfficialWebsitesPage));
        return Task.CompletedTask;
    }
}
