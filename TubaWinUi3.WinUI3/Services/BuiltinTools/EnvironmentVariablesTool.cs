namespace TubaWinUi3.Services;

/// <summary>「环境变量」内置工具：跳转到 <see cref="TubaWinUi3.Pages.EnvironmentVariablesPage"/>。</summary>
public sealed class EnvironmentVariablesTool : IBuiltinTool
{
    public string Id => "environment-variables";
    public string Name => LocalizationService.L("Builtin_environment-variables_Name", "环境变量");
    public string Description => LocalizationService.L("Builtin_environment-variables_Desc", "查看与编辑用户 / 系统环境变量，PATH 支持逐条可视化编辑，保存前自动备份、保存后广播刷新。");
    public string Glyph => "\uE756";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.EnvironmentVariablesPage));
        return Task.CompletedTask;
    }
}
