namespace TubaWinUi3.Services;

/// <summary>「文件占用查看」内置工具：跳转到 <see cref="TubaWinUi3.Pages.FileLockPage"/>。</summary>
public sealed class FileLocksmithTool : IBuiltinTool
{
    public string Id => "file-locksmith";
    public string Name => LocalizationService.L("Builtin_file-locksmith_Name", "文件占用查看");
    public string Description => LocalizationService.L("Builtin_file-locksmith_Desc", "查看哪个进程正在占用指定的文件或目录，展开可看具体持有的路径，并支持结束占用进程。");
    public string Glyph => "\uE72E";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.FileLockPage));
        return Task.CompletedTask;
    }
}
