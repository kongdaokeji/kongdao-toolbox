using System.Runtime.InteropServices;

namespace TubaWinUi3.Services;

/// <summary>
/// Office / WPS COM 互联：处理内置轻量引擎无法解析的旧版二进制格式
/// （.doc / .wps / .ppt / .dps / .et，以及需要 Office 参与的转换）。
/// 自动探测 Microsoft Office（Word/Excel/PowerPoint）与 WPS Office
/// （KWPS/KET/KWPP），在专用 STA 线程上执行，3 分钟超时。
/// </summary>
public static class OfficeInteropService
{
    private static readonly string[] WordProgIds = { "Word.Application", "KWPS.Application", "WPS.Application" };
    private static readonly string[] ExcelProgIds = { "Excel.Application", "KET.Application", "ET.Application" };
    private static readonly string[] PptProgIds = { "PowerPoint.Application", "KWPP.Application", "WPP.Application" };

    private const int TimeoutMinutes = 3;

    public static bool IsWordAvailable => ResolveProgId(WordProgIds) is not null;
    public static bool IsExcelAvailable => ResolveProgId(ExcelProgIds) is not null;
    public static bool IsPptAvailable => ResolveProgId(PptProgIds) is not null;

    /// <summary>该文件是否可由当前机器上的 Office/WPS 处理（按文件家族探测）。</summary>
    public static bool IsAvailableFor(string sourcePath)
        => FamilyOf(sourcePath) switch
        {
            "word" => IsWordAvailable,
            "excel" => IsExcelAvailable,
            "ppt" => IsPptAvailable,
            _ => false
        };

    /// <summary>
    /// 通过 Office/WPS 把 source 另存为 targetExt（.docx/.pdf/.html/.txt/.xlsx/.csv/.pptx）。
    /// 返回输出文件列表（单个输出路径由调用方传入）。
    /// </summary>
    public static Task<List<string>> ConvertAsync(string source, string targetExt,
        string outputPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var family = FamilyOf(source)
            ?? throw new NotSupportedException($"不支持的 Office 互联格式：{Path.GetExtension(source)}");
        var format = FileFormatFor(family, targetExt)
            ?? throw new NotSupportedException(
                $"Office 互联不支持的转换目标：{Path.GetExtension(source)} → {targetExt}（可先转 PDF/DOCX 再继续）");

        var progIds = family switch
        {
            "word" => WordProgIds,
            "excel" => ExcelProgIds,
            _ => PptProgIds
        };
        var appName = family switch { "word" => "Word", "excel" => "Excel", _ => "PowerPoint" };

        return Task.Run(() => RunSta(() =>
        {
            var type = ResolveProgId(progIds)
                ?? throw new InvalidOperationException($"未安装可处理 {Path.GetExtension(source)} 的 Office / WPS 组件（{appName}）");

            progress?.Report($"正在通过 {appName} 转换 {Path.GetFileName(source)}…");
            dynamic app;
            try
            {
                app = Activator.CreateInstance(type)!;
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException(
                    $"启动 {appName} 失败（{ex.Message}）。若反复失败，请尝试以普通权限运行本工具或直接用 Office 打开该文件另存。");
            }

            try
            {
                TrySet(() => app.Visible = false);
                TrySet(() => app.DisplayAlerts = 0);
                TrySet(() => app.AutomationSecurity = 3); // msoAutomationSecurityForceDisable：禁用宏

                switch (family)
                {
                    case "word":
                    {
                        dynamic doc = app.Documents.Open(source);
                        try { SaveAs(doc, outputPath, format); }
                        finally { TrySet(() => doc.Close(false)); }
                        break;
                    }
                    case "excel":
                    {
                        dynamic wb = app.Workbooks.Open(source);
                        try { SaveAs(wb, outputPath, format); }
                        finally { TrySet(() => wb.Close(false)); }
                        break;
                    }
                    default:
                    {
                        dynamic pres = app.Presentations.Open(source, true, false, false);
                        try { SaveAs(pres, outputPath, format); }
                        finally { TrySet(() => pres.Close()); }
                        break;
                    }
                }
            }
            finally
            {
                TrySet(() => app.Quit());
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                throw new InvalidOperationException($"{appName} 转换未产生输出文件");
            return new List<string> { outputPath };
        }), ct);
    }

    /// <summary>SaveAs2 优先，旧组件（WPS）回退 SaveAs。</summary>
    private static void SaveAs(dynamic doc, string path, int format)
    {
        try
        {
            doc.SaveAs2(path, format);
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            doc.SaveAs(path, format);
        }
    }

    /// <summary>COM 属性设置失败不影响主流程（不同组件支持度不同）。</summary>
    private static void TrySet(Action action)
    {
        try { action(); }
        catch { /* 忽略：WPS/Office 版本差异 */ }
    }

    private static string? FamilyOf(string source)
        => Path.GetExtension(source).ToLowerInvariant() switch
        {
            ".doc" or ".wps" or ".rtf" or ".odt" => "word",
            ".xls" or ".et" => "excel",
            ".ppt" or ".dps" or ".odp" => "ppt",
            _ => null
        };

    /// <summary>文件家族 → 目标扩展名的 SaveAs FileFormat 常量。</summary>
    private static int? FileFormatFor(string family, string targetExt) => (family, targetExt) switch
    {
        // Word: wdFormatXMLDocument=12, wdFormatPDF=17, wdFormatHTML=8, wdFormatUnicodeText=7
        ("word", ".docx") => 12,
        ("word", ".pdf") => 17,
        ("word", ".html") => 8,
        ("word", ".txt") => 7,
        // Excel: xlOpenXMLWorkbook=51, xlPDF=57, xlHtml=44, xlCSV=6
        ("excel", ".xlsx") => 51,
        ("excel", ".pdf") => 57,
        ("excel", ".html") => 44,
        ("excel", ".csv") => 6,
        // PowerPoint: ppSaveAsOpenXMLPresentation=24, ppSaveAsPDF=32
        ("ppt", ".pptx") => 24,
        ("ppt", ".pdf") => 32,
        _ => null
    };

    private static Type? ResolveProgId(string[] progIds)
    {
        foreach (var progId in progIds)
        {
            try
            {
                var type = Type.GetTypeFromProgID(progId);
                if (type is not null) return type;
            }
            catch
            {
                // 组件注册异常视为不可用
            }
        }
        return null;
    }

    private static T RunSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        })
        { IsBackground = true, Name = "office-interop" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(TimeoutMinutes)))
            throw new TimeoutException($"Office / WPS 转换超时（{TimeoutMinutes} 分钟无响应），已放弃");
        if (error is not null)
            throw new InvalidOperationException($"Office / WPS 转换失败：{error.Message}", error);
        return result;
    }
}
