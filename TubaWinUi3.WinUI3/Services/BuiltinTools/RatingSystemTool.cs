using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class RatingSystemTool : IBuiltinTool
{
	public string Id => "rating-system";
	public string Name => "硬件评分";
	public string Description => "为你的笔记本或台式机硬件打分，查看社区排行榜对比评价。";
	public string Glyph => "\ue735";
	public string Category => "硬件工具";
	public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

	public Task ExecuteAsync(BuiltinToolContext context)
	{
		App.MainWindow?.NavigateToToolPage(typeof(RatingSystemPage));
		return Task.CompletedTask;
	}
}
