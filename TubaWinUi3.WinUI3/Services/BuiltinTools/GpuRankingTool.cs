using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class GpuRankingTool : IBuiltinTool
{
    public string Id => "gpu-ranking";
    public string Name => "GPU 天梯图";
    public string Description => "查看桌面/笔记本 GPU 性能天梯图，支持品牌筛选与排序。数据来自 NanoReview";
    public string Glyph => "\uE9D5";
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(GpuRankingPage));
        return Task.CompletedTask;
    }
}
