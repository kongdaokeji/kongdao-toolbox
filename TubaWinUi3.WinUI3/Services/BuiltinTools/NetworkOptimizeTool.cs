namespace TubaWinUi3.Services;

public sealed class NetworkOptimizeTool : IBuiltinTool
{
    public string Id => "network-optimize";
    public string Name => "网络优化";
    public string Description => "TCP 参数优化（拥塞控制 / Chimney / Nagle / 网卡节能）、DNS 延迟测速与配置、公网 IP 查询、网络重置与 DHCP 修复";
    public string Glyph => "\uE968";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.NetworkOptimizePage));
        return Task.CompletedTask;
    }
}