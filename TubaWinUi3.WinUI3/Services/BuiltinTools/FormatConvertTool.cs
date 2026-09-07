namespace TubaWinUi3.Services;

public sealed class FormatConvertTool : IBuiltinTool
{
    public string Id => "format-converter";
    public string Name => "格式转换";
    public string Description => "图片/音视频/Word/Excel/PPT/PDF/文本互转，OCR 识别、PDF 合并拆分、任意文件打包 ZIP，批量队列、拖入即用";
    public string Glyph => "\uE8B2";
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.FormatConverterPage));
        return Task.CompletedTask;
    }
}