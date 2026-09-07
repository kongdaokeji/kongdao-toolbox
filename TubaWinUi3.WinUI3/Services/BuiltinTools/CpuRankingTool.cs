using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class CpuRankingTool : IBuiltinTool
{
    public string Id => "cpu-ranking";
    public string Name => "CPU 天梯图";
    public string Description => "查看桌面/笔记本 CPU 性能天梯图，支持品牌筛选与排序。数据来自 NanoReview";
    public string Glyph => "\uEEA1";
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(CpuRankingPage));
        return Task.CompletedTask;
    }
}
