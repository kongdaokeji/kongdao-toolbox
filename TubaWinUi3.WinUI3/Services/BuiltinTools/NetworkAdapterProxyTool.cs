namespace TubaWinUi3.Services;

public sealed class NetworkAdapterProxyTool : IBuiltinTool
{
    public string Id => "network-adapter-proxy";
    public string Name => "网络调度器";
    public string Description => "汇聚多网络适配器，智能分配流量，Wi-Fi 有线自动加速。";
    public string Glyph => "\uE774";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.NetworkAdapterProxyPage));
        return Task.CompletedTask;
    }
}
