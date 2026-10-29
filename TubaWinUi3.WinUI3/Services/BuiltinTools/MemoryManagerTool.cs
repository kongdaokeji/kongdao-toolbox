namespace TubaWinUi3.Services;

public sealed class MemoryManagerTool : IBuiltinTool
{
    public string Id => "memory-manager";
    public string Name => LocalizationService.L("Builtin_memory-manager_Name", "内存管理");
    public string Description => LocalizationService.L("Builtin_memory-manager_Desc", "查看内存占用和细分容量，简单整理缓存，分步迁移分页文件并支持备份恢复。");
    public string Glyph => "\uE964";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;
    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(Pages.MemoryManagerPage));
        return Task.CompletedTask;
    }
}
