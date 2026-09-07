using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>
/// 流氓软件的克星：扫描和清理 Windows 流氓右键菜单、自启动、计划任务、服务、
/// 浏览器插件和文件关联残留。移植自 https://github.com/aakk007/RogueCleaner（MIT）。
/// </summary>
public sealed class RogueCleanerTool : IBuiltinTool
{
    public string Id => "rogue-cleaner";
    public string Name => "流氓软件的克星";
    public string Description => "扫描和清理流氓右键菜单、自启动、计划任务、服务、浏览器插件和文件关联残留，含恢复中心";
    public string Glyph => "\uE72E";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.ProgressTask;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(RogueCleanerPage), "contextmenu");
        return Task.CompletedTask;
    }
}
