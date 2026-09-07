using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class CommunityToolBuiltinTool : IBuiltinTool
{
	public string Id => "community-tools";
	public string Name => "社区工具";
	public string Description => "来自社区贡献的工具插件，下载安装即可使用。支持提交和删除工具。";
	public string Glyph => "\ue774";
	public string Category => "实用工具";
	public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

	public Task ExecuteAsync(BuiltinToolContext context)
	{
		App.MainWindow?.NavigateToToolPage(typeof(CommunityToolsPage));
		return Task.CompletedTask;
	}
}
