namespace TubaWinUi3.Services;

public sealed class DisplayToolsTool : IBuiltinTool
{
    public string Id => "display-tools";
    public string Name => LocalizationService.L("Builtin_display-tools_Name", "屏幕工具");
    public string Description => LocalizationService.L("Builtin_display-tools_Desc", "查看多显示器、EDID、色域覆盖、HDR、颜色配置文件，复制检测报告并进行全屏坏点与色阶测试。");
    public string Glyph => "\uE7F4";
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;
    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.DisplayToolsPage));
        return Task.CompletedTask;
    }
}
