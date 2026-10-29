namespace TubaWinUi3.Services;

/// <summary>「句柄清理」内置工具：跳转到 <see cref="TubaWinUi3.Pages.HandleCleanerPage"/>。</summary>
public sealed class HandleCleanerTool : IBuiltinTool
{
    public string Id => "handle-cleaner";
    public string Name => LocalizationService.L("Builtin_handle-cleaner_Name", "句柄清理");
    public string Description => LocalizationService.L("Builtin_handle-cleaner_Desc", "扫描全系统句柄占用与疑似泄漏进程，一键安全清理指向已删除文件的失效句柄，缓解长期开机后的系统卡顿。");
    public string Glyph => "\uE777";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.HandleCleanerPage));
        return Task.CompletedTask;
    }
}
