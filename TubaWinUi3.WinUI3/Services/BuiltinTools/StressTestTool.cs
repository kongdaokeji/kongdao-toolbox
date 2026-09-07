using Microsoft.UI.Xaml;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;

namespace TubaWinUi3.Services;

public sealed class StressTestTool : IBuiltinTool
{
    public string Id => "stress-test";
    public string Name => "一键三烤";
    public string Description => "CPU / GPU / 网卡压力测试工具，自由勾选烤机项目（CPU、GPU、网卡），网卡烤机支持自定义数据量与速率参考，实时监控温度、频率、功耗与网卡吞吐。";
    public string Glyph => "\uECAD";
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(StressTestPage));
        return Task.CompletedTask;
    }
}
