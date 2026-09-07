using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class BatteryAnalyzerTool : IBuiltinTool
{
    public string Id => "battery-analyzer";
    public string Name => "电池消耗分析";
    public string Description => "分析电池消耗趋势、应用耗电排行，比 Windows 设置更强大的电池分析工具";
    public string Glyph => "\uE85E";
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(BatteryAnalyzerPage));
        return Task.CompletedTask;
    }
}
