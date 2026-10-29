using System.IO.Compression;
using System.Text;
using HtmlAgilityPack;

namespace TubaWinUi3.Services;

/// <summary>
/// 轻量 DOCX 生成器：HTML / 纯文本 → 最小可用的 .docx（手写 OpenXML 包，
/// 无第三方依赖）。支持标题、粗斜体、代码字体、列表前缀、引用缩进与简单表格。
/// 纯逻辑，可单元测试。
/// </summary>
public static class DocxWriter
{
    // ── 块模型 ──

    private sealed record Run(string Text, bool Bold, bool Italic, bool Mono);

    private sealed class Para
    {
        public string? Style;          // Heading1..6 / null=Normal
        public bool Quote;             // 引用缩进
        public bool Numbered;          // 项目符号列表（numbering.xml numId=1）
        public List<Run> Runs = [];
    }

    private sealed class TableBlock
    {
        public List<string[]> Rows = [];
    }

    // ── 公开入口 ──

    /// <summary>HTML → DOCX 字节。</summary>
    public static byte[] FromHtml(string html, string? title = null)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        var blocks = new List<object>();
        CollectBlocks(body, blocks);
        return BuildDocx(blocks, title);
    }

    /// <summary>纯文本 → DOCX 字节（每行一段，空行为段间距）。</summary>
    public static byte[] FromText(string text, string? title = null)
    {
        var blocks = new List<object>();
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Trim().Length == 0) continue;
            blocks.Add(new Para { Runs = [new Run(line, false, false, false)] });
        }
        return BuildDocx(blocks, title);
    }

    // ── HTML → 块 ──

    private static void CollectBlocks(HtmlNode container, List<object> blocks)
    {
        foreach (var node in container.ChildNodes)
            CollectNode(node, blocks);
    }

    private static void CollectNode(HtmlNode node, List<object> blocks)
    {
        if (node.NodeType == HtmlNodeType.Text || node.NodeType == HtmlNodeType.Comment) return;
        var name = node.Name.ToLowerInvariant();

        switch (name)
        {
            case "script" or "style" or "head" or "noscript":
                return;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
            {
                var para = new Para { Style = "Heading" + (name[1] - '0') };
                CollectInline(node, para.Runs, false, false);
                if (para.Runs.Count > 0) blocks.Add(para);
                return;
            }
            case "p":
            {
                var para = new Para();
                CollectInline(node, para.Runs, false, false);
                if (para.Runs.Count > 0) blocks.Add(para);
                return;
            }
            case "pre":
            {
                var text = node.InnerText.TrimEnd();
                if (text.Length == 0) return;
                var para = new Para();
                // 按行拆分为多段（Word 中换行用多个段落更稳）
                foreach (var line in text.Split('\n'))
                    para.Runs.Add(new Run(line.TrimEnd(), false, false, true));
                blocks.Add(para);
                return;
            }
            case "blockquote":
            {
                var para = new Para { Quote = true };
                CollectInline(node, para.Runs, false, italic: true);
                if (para.Runs.Count > 0) blocks.Add(para);
                return;
            }
            case "ul" or "ol":
            {
                int index = 1;
                foreach (var li in node.Elements("li"))
                {
                    var para = new Para();
                    if (name == "ul")
                    {
                        // 无序列表：Word 原生项目符号（读取时可识别为列表）
                        para.Numbered = true;
                    }
                    else
                    {
                        // 有序列表：直接以「N. 」文本前缀（Markdown 中即为合法有序列表）
                        para.Runs.Add(new Run($"{index++}. ", false, false, false));
                    }
                    CollectInline(li, para.Runs, false, false);
                    if (para.Runs.Count > (name == "ul" ? 0 : 1)) blocks.Add(para);
                }
                return;
            }
            case "table":
            {
                var table = new TableBlock();
                foreach (var tr in node.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
                {
                    var cells = tr.Elements("td").Concat(tr.Elements("th"))
                        .Select(td => HtmlToPlainText(td).Trim())
                        .ToArray();
                    if (cells.Length > 0) table.Rows.Add(cells);
                }
                if (table.Rows.Count > 0) blocks.Add(table);
                return;
            }
            case "hr":
                blocks.Add(new Para { Runs = [new Run("──────────", false, false, false)] });
                return;
            default:
                // div / body / 未知容器：下钻
                if (node.ChildNodes.Any(n => n.NodeType == HtmlNodeType.Element && IsBlock(n)))
                    CollectBlocks(node, blocks);
                else
                {
                    var para = new Para();
                    CollectInline(node, para.Runs, false, false);
                    if (para.Runs.Count > 0) blocks.Add(para);
                }
                return;
        }
    }

    private static void CollectInline(HtmlNode node, List<Run> runs, bool bold, bool italic)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
            {
                var text = CollapseSpace(child.InnerText);
                if (text.Length > 0) runs.Add(new Run(text, bold, italic, false));
                continue;
            }
            if (child.NodeType == HtmlNodeType.Comment) continue;
            switch (child.Name.ToLowerInvariant())
            {
                case "br":
                    runs.Add(new Run("\n", bold, italic, false));
                    break;
                case "strong" or "b":
                    CollectInline(child, runs, true, italic);
                    break;
                case "em" or "i":
                    CollectInline(child, runs, bold, true);
                    break;
                case "code" or "kbd" or "samp":
                {
                    var text = CollapseSpace(child.InnerText);
                    if (text.Length > 0) runs.Add(new Run(text, bold, italic, true));
                    break;
                }
                case "a":
                {
                    var text = child.InnerText;
                    if (text.Trim().Length > 0) runs.Add(new Run(CollapseSpace(text), bold, italic, false));
                    break;
                }                case "img":
                {
                    var alt = child.GetAttributeValue("alt", "");
                    runs.Add(new Run(string.IsNullOrWhiteSpace(alt) ? "[图片]" : $"[图片:{alt}]", bold, italic, false));
                    break;
                }
                case "script" or "style":
                    break;
                default:
                    CollectInline(child, runs, bold, italic);
                    break;
            }
        }
    }

    /// <summary>块级元素内的纯文本（供表格单元格）。</summary>
    private static string HtmlToPlainText(HtmlNode node)
    {
        var sb = new StringBuilder();
        void Walk(HtmlNode n)
        {
            if (n.NodeType == HtmlNodeType.Text) sb.Append(CollapseSpace(HtmlEntity.DeEntitize(n.InnerText)));
            else if (n.NodeType == HtmlNodeType.Comment) { }
            else if (n.Name.Equals("br", StringComparison.OrdinalIgnoreCase)) sb.Append(' ');
            else foreach (var c in n.ChildNodes) Walk(c);
        }
        foreach (var c in node.ChildNodes) Walk(c);
        return sb.ToString();
    }

    private static readonly HashSet<string> BlockNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "table", "pre",
        "blockquote", "hr", "section", "article", "header", "footer", "figure"
    };

    private static bool IsBlock(HtmlNode node)
        => node.NodeType == HtmlNodeType.Element && BlockNames.Contains(node.Name);

    private static string CollapseSpace(string text)
        => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    // ── DOCX 打包 ──

    private static byte[] BuildDocx(List<object> blocks, string? title)
    {
        var bodySb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title))
            bodySb.Append(ParaXml(new Para { Style = "Title", Runs = [new Run(title, false, false, false)] }));
        foreach (var block in blocks)
        {
            if (block is Para p)
                bodySb.Append(ParaXml(p));
            else if (block is TableBlock t)
                bodySb.Append(TableXml(t));
        }

        var documentXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
            <w:body>
            {bodySb}
            <w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
            </w:body>
            </w:document>
            """;

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            WriteEntry(zip, "[Content_Types].xml", ContentTypesXml);
            WriteEntry(zip, "_rels/.rels", RelsXml);
            WriteEntry(zip, "word/_rels/document.xml.rels", DocumentRelsXml);
            WriteEntry(zip, "word/styles.xml", StylesXmlCached);
            WriteEntry(zip, "word/numbering.xml", NumberingXml);
            WriteEntry(zip, "word/document.xml", documentXml);
        }
        return ms.ToArray();
    }

    private static string ParaXml(Para para)
    {
        var sb = new StringBuilder("<w:p><w:pPr>");
        if (para.Style is not null)
            sb.Append($"<w:pStyle w:val=\"{para.Style}\"/>");
        if (para.Numbered)
            sb.Append("<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>");
        if (para.Quote)
            sb.Append("<w:ind w:left=\"720\"/>");
        sb.Append("</w:pPr>");
        foreach (var run in para.Runs)
            sb.Append(RunXml(run));
        sb.Append("</w:p>");
        return sb.ToString();
    }

    private static string RunXml(Run run)
    {
        var sb = new StringBuilder("<w:r>");
        if (run.Bold || run.Italic || run.Mono)
        {
            sb.Append("<w:rPr>");
            if (run.Bold) sb.Append("<w:b/>");
            if (run.Italic) sb.Append("<w:i/>");
            if (run.Mono) sb.Append("<w:rFonts w:ascii=\"Consolas\" w:eastAsia=\"Consolas\" w:hAnsi=\"Consolas\"/>");
            sb.Append("</w:rPr>");
        }
        sb.Append($"<w:t xml:space=\"preserve\">{XmlEscape(run.Text)}</w:t></w:r>");
        return sb.ToString();
    }

    private static string TableXml(TableBlock table)
    {
        var sb = new StringBuilder();
        sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblBorders>");
        foreach (var side in new[] { "top", "left", "bottom", "right", "insideH", "insideV" })
            sb.Append($"<w:{side} w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
        sb.Append("</w:tblBorders></w:tblPr>");
        for (int r = 0; r < table.Rows.Count; r++)
        {
            sb.Append("<w:tr>");
            if (r == 0) sb.Append("<w:trPr><w:tblHeader/></w:trPr>");
            foreach (var cell in table.Rows[r])
            {
                sb.Append("<w:tc><w:tcPr><w:tcW w:w=\"0\" w:type=\"auto\"/></w:tcPr>");
                sb.Append($"<w:p><w:r>{(r == 0 ? "<w:rPr><w:b/></w:rPr>" : "")}" +
                          $"<w:t xml:space=\"preserve\">{XmlEscape(cell)}</w:t></w:r></w:p>");
                sb.Append("</w:tc>");
            }
            sb.Append("</w:tr>");
        }
        sb.Append("</w:tbl>");
        // 表后补一个空段，避免紧跟的表格粘连
        sb.Append("<w:p/>");
        return sb.ToString();
    }

    private static string XmlEscape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("\r", "");

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private const string ContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
        <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
        <Default Extension="xml" ContentType="application/xml"/>
        <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
        <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
        </Types>
        """;

    private const string RelsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
        <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    private const string DocumentRelsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
        <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
        </Relationships>
        """;

    private const string NumberingXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
        <w:abstractNum w:abstractNumId="0">
        <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="&#xF0B7;"/><w:lvlJc w:val="left"/>
        <w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr>
        <w:rPr><w:rFonts w:ascii="Symbol" w:hAnsi="Symbol" w:hint="default"/></w:rPr></w:lvl>
        </w:abstractNum>
        <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
        </w:numbering>
        """;

    private static string StylesXml()
    {
        var sb = new StringBuilder();
        sb.Append("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
            <w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:eastAsia="Microsoft YaHei" w:hAnsi="Calibri"/><w:sz w:val="22"/></w:rPr></w:rPrDefault></w:docDefaults>
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
            """);
        // 标题字号（半磅）：H1 32 / H2 28 / H3 24 / H4 22 / H5 21 / H6 20
        int[] sizes = { 32, 28, 24, 22, 21, 20 };
        for (int i = 0; i < sizes.Length; i++)
        {
            var level = i + 1;
            sb.Append($"""
                <w:style w:type="paragraph" w:styleId="Heading{level}"><w:name w:val="heading {level}"/><w:basedOn w:val="Normal"/><w:qFormat/>
                <w:pPr><w:keepNext/><w:outlineLvl w:val="{i}"/><w:spacing w:before="280" w:after="120"/></w:pPr>
                <w:rPr><w:b/><w:sz w:val="{sizes[i]}"/></w:rPr></w:style>
                """);
        }
        sb.Append("""
            <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:qFormat/>
            <w:pPr><w:spacing w:after="240"/></w:pPr><w:rPr><w:b/><w:sz w:val="44"/></w:rPr></w:style>
            </w:styles>
            """);
        return sb.ToString();
    }

    private static readonly string StylesXmlCached = StylesXml();
}
