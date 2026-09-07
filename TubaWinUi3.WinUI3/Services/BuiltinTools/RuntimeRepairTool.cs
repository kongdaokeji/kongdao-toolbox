namespace TubaWinUi3.Services;

public sealed class RuntimeRepairTool : IBuiltinTool
{
    public string Id => "runtime-repair";
    public string Name => "运行库修复";
    public string Description => "检测并修复缺失的 Visual C++ 2008-2026、.NET Framework 4.8.1、DirectX 旧版游戏组件，微软官方源下载 + 签名校验";
    public string Glyph => "\uE90F";
    public string Category => "游戏工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.RuntimeRepairPage));
        return Task.CompletedTask;
    }
}