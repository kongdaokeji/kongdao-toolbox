using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class TrafficMonitorTool : IBuiltinTool
{
    public string Id => "traffic-monitor";
    public string Name => "流量监控器";
    public string Description => "选择网卡实时查看各连接的流量、速度与延迟，整卡吞吐折线统计，支持快照录制与滑条回放。";
    public string Glyph => "\uE774";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TrafficMonitorPage));
        return Task.CompletedTask;
    }
}
