using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace TubaWinUi3.Services;

/// <summary>
/// 轻量 DOCX 读取器：解包 word/document.xml，提取段落/标题/列表/粗斜体/表格，
/// 输出 Markdown 或纯文本。纯逻辑，可单元测试（配合 DocxWriter 可做往返测试）。
/// </summary>
public static class DocxReader
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>docx 文件 → Markdown。</summary>
    public static string ToMarkdown(string docxPath) => Render(docxPath, markdown: true);

    /// <summary>docx 文件 → 纯文本。</summary>
    public static string ToPlainText(string docxPath) => Render(docxPath, markdown: false);

    private static string Render(string docxPath, bool markdown)
    {
        using var zip = ZipFile.OpenRead(docxPath);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("不是有效的 .docx 文件（缺少 word/document.xml）");
        XDocument doc;
        using (var stream = entry.Open())
        {
            doc = XDocument.Load(stream);
        }
        var body = doc.Root?.Element(W + "body")
            ?? throw new InvalidOperationException("docx 内容为空");

        var sb = new StringBuilder();
        foreach (var element in body.Elements())
        {
            if (element.Name == W + "p")
                RenderParagraph(element, sb, markdown);
            else if (element.Name == W + "tbl")
                RenderTable(element, sb, markdown);
        }
        var text = sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        // 压缩多余空行
        while (text.Contains("\n\n\n", StringComparison.Ordinal))
            text = text.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        return text.Trim('\n', ' ');
    }

    private static void RenderParagraph(XElement p, StringBuilder sb, bool markdown)
    {
        var style = p.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value ?? "";
        var isListItem = p.Element(W + "pPr")?.Element(W + "numPr") is not null;
        var runs = new StringBuilder();
        foreach (var run in p.Elements(W + "r"))
            RenderRun(run, runs, markdown);
        var text = runs.ToString().Trim();
        if (text.Length == 0) return;

        if (markdown)
        {
            if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(style.AsSpan("Heading".Length), out var level)
                && level is >= 1 and <= 6)
                sb.Append(new string('#', level) + " " + text + "\n\n");
            else if (style == "Title")
                sb.Append("# " + text + "\n\n");
            else if (isListItem)
                sb.Append("- " + text + "\n");
            else if (style is "Quote" or "IntenseQuote")
                sb.Append("> " + text + "\n\n");
            else
                sb.Append(text + "\n\n");
        }
        else
        {
            sb.Append(text + "\n");
        }
    }

    private static void RenderRun(XElement run, StringBuilder sb, bool markdown)
    {
        var props = run.Element(W + "rPr");
        bool bold = props?.Element(W + "b") is not null;
        bool italic = props?.Element(W + "i") is not null;
        foreach (var child in run.Elements())
        {
            if (child.Name == W + "t")
            {
                var text = child.Value;
                if (text.Length == 0) continue;
                if (markdown)
                {
                    if (bold && italic) sb.Append($"***{text}***");
                    else if (bold) sb.Append($"**{text}**");
                    else if (italic) sb.Append($"*{text}*");
                    else sb.Append(text);
                }
                else sb.Append(text);
            }
            else if (child.Name == W + "tab")
            {
                sb.Append('\t');
            }
            else if (child.Name == W + "br")
            {
                sb.Append('\n');
            }
            else if (child.Name == W + "drawing" || child.Name == W + "pict")
            {
                sb.Append(markdown ? "![图片]()" : "[图片]");
            }
        }
    }

    private static void RenderTable(XElement tbl, StringBuilder sb, bool markdown)
    {
        var rows = new List<string[]>();
        foreach (var tr in tbl.Elements(W + "tr"))
        {
            var cells = tr.Elements(W + "tc").Select(tc =>
                string.Join(" ", tc.Elements(W + "p").Select(CellText).Where(t => t.Length > 0))).ToArray();
            if (cells.Length > 0) rows.Add(cells);
        }
        if (rows.Count == 0) return;

        if (!markdown)
        {
            foreach (var row in rows)
                sb.Append(string.Join("\t", row) + "\n");
            return;
        }

        var width = rows.Max(r => r.Length);
        for (int r = 0; r < rows.Count; r++)
        {
            sb.Append("| ");
            for (int c = 0; c < width; c++)
                sb.Append((c < rows[r].Length ? rows[r][c] : "").Replace("|", "\\|", StringComparison.Ordinal) + (c < width - 1 ? " | " : ""));
            sb.Append(" |\n");
            if (r == 0)
                sb.Append("| " + string.Join(" | ", Enumerable.Repeat("---", width)) + " |\n");
        }
        sb.Append('\n');
    }

    private static string CellText(XElement p)
    {
        var sb = new StringBuilder();
        foreach (var run in p.Elements(W + "r"))
            RenderRun(run, sb, markdown: false);
        return sb.ToString().Trim();
    }
}
