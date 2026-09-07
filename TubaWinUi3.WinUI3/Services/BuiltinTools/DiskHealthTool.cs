namespace TubaWinUi3.Services;

public sealed class DiskHealthTool : IBuiltinTool
{
    public string Id => "disk-health";
    public string Name => "磁盘健康";
    public string Description => "SMART 健康度检测（CrystalDiskInfo 方案）：温度/通电/寿命/读写量，支持 SSD TRIM 与机械盘碎片整理";
    public string Glyph => "\uEDA8";
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.DiskHealthPage));
        return Task.CompletedTask;
    }
}