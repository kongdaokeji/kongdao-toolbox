using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class SpeedTestTool : IBuiltinTool
{
    public string Id => "speed-test";
    public string Name => "网速测试";
    public string Description => "基于浙大测速节点，原生测试网络延迟、下载与上传速度";
    public string Glyph => "\uE86F";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(SpeedTestPage));
        return Task.CompletedTask;
    }
}
