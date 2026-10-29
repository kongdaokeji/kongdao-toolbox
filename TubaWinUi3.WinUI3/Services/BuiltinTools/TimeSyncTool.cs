namespace TubaWinUi3.Services;

public sealed class TimeSyncTool : IBuiltinTool
{
    public string Id => "time-sync";
    public string Name => LocalizationService.L("Builtin_time-sync_Name", "时间同步");
    public string Description => LocalizationService.L("Builtin_time-sync_Desc", "一键切换系统 NTP 时间服务器（阿里云 / 腾讯云 / 国家授时中心…），支持服务器测速、偏差检测、立即校时、时间服务修复与恢复系统默认");
    public string Glyph => "\uE823";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.TimeSyncPage));
        return Task.CompletedTask;
    }
}
