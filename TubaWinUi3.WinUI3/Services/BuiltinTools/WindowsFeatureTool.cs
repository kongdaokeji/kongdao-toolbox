namespace TubaWinUi3.Services;

public sealed class WindowsFeatureTool : IBuiltinTool
{
    public string Id => "windows-feature";
    public string Name => "Windows 隐藏功能";
    public string Description => "查询、启用、禁用、重置 Windows 实验性功能开关（ViVe 引擎移植，ntdll 功能配置 API 直读，毫秒级响应）";
    public string Glyph => "\uE950";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.WindowsFeaturePage));
        return Task.CompletedTask;
    }
}