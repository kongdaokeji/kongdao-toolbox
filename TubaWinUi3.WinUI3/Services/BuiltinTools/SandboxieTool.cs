using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>
/// 恶意软件沙盒（Sandboxie-Plus）：在隔离沙盒中运行可疑程序 / 恶意样本，
/// 所有写入停留沙盒内，删除沙盒内容即可还原。页面提供按架构下载安装包与使用教程。
/// </summary>
public sealed class SandboxieTool : IBuiltinTool
{
    public string Id => "sandboxie";
    public string Name => "恶意软件沙盒";
    public string Description => "Sandboxie-Plus 沙盒环境，安全运行和分析可疑程序 / 恶意软件，删除沙盒即可还原系统";
    public string Glyph => "\uEA18";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.ProgressTask;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(SandboxiePage));
        return Task.CompletedTask;
    }
}
