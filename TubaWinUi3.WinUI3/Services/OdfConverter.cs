using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace TubaWinUi3.Services;

/// <summary>
/// 轻量 ODF 转换器：odt / ods / odp → HTML（解包 content.xml 手工遍历，
/// 内嵌图片以 base64 内联，输出与 pptx/docx 转换一致的 HTML 片段）。
/// 纯逻辑，可单元测试。
/// </summary>
public static class OdfConverter
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    /// <summary>ODF 文件（odt/ods/odp）→ HTML 片段。</summary>
    public static string ToHtml(string odfPath)
    {
        using var zip = ZipFile.OpenRead(odfPath);
        return ToHtml(zip);
    }

    /// <summary>从已打开的 zip 读取 content.xml 并转换。</summary>
    public static string ToHtml(ZipArchive zip)
    {
        var entry = zip.GetEntry("content.xml")
            ?? throw new InvalidOperationException("不是有效的 ODF 文件（缺少 content.xml）");
        XDocument doc;
        using (var stream = entry.Open())
        {
            doc = XDocument.Load(stream);
        }
        var body = doc.Root?.Element(Office + "body")
            ?? throw new InvalidOperationException("ODF 内容为空");

        var sb = new StringBuilder();
        var text = body.Element(Office + "text");
        var spreadsheet = body.Element(Office + "spreadsheet");
        var presentation = body.Element(Office + "presentation");

        if (text is not null)
        {
            sb.Append("<div class=\"odt\">");
            RenderTextBody(text, sb, zip);
            sb.Append("</div>");
        }
        else if (spreadsheet is not null)
        {
            sb.Append("<div class=\"ods\">");
            foreach (var tableEl in spreadsheet.Elements(Table + "table"))
                RenderSheet(tableEl, sb);
            sb.Append("</div>");
        }
        else if (presentation is not null)
        {
            sb.Append("<div class=\"odp\">");
            foreach (var page in presentation.Elements(Draw + "page"))
                RenderSlide(page, sb, zip);
            sb.Append("</div>");
        }
        else
        {
            throw new InvalidOperationException("无法识别的 ODF 文档类型（仅支持 odt / ods / odp）");
        }
        return sb.ToString();
    }

    // ── odt：正文 ──

    private static void RenderTextBody(XElement container, StringBuilder sb, ZipArchive zip)
    {
        foreach (var el in container.Elements())
        {
            if (el.Name == Text + "h")
            {
                var level = (int?)el.Attribute(Text + "outline-level") ?? 1;
                var levelClamped = Math.Clamp(level, 1, 6);
                sb.Append($"<h{levelClamped}>{Escape(el.Value)}</h{levelClamped}>");
            }
            else if (el.Name == Text + "p")
            {
                var inner = RenderInline(el, zip);
                if (inner.Length > 0) sb.Append($"<p>{inner}</p>");
            }
            else if (el.Name == Text + "list")
            {
                sb.Append("<ul>");
                foreach (var item in el.Elements(Text + "list-item"))
                {
                    sb.Append("<li>");
                    foreach (var p in item.Elements(Text + "p"))
                        sb.Append(RenderInline(p, zip) + "<br/>");
                    // 嵌套列表
                    foreach (var nested in item.Elements(Text + "list"))
                        RenderTextBody(nested, sb, zip);
                    sb.Append("</li>");
                }
                sb.Append("</ul>");
            }
            else if (el.Name == Draw + "frame")
            {
                RenderFrame(el, sb, zip);
            }
            else if (el.Name == Text + "section" || el.Name == Text + "table-of-content")
            {
                RenderTextBody(el, sb, zip);
            }
            else if (el.Name == Table + "table")
            {
                RenderSheet(el, sb);
            }
        }
    }

    /// <summary>段落内的行内内容（含超链接/图片/空格填充）。zip 为 null 时跳过图片。</summary>
    private static string RenderInline(XElement p, ZipArchive? zip)
    {
        var sb = new StringBuilder();
        foreach (var node in p.Nodes())
        {
            if (node is XText textNode)
            {
                sb.Append(Escape(textNode.Value));
                continue;
            }
            if (node is not XElement el) continue;
            if (el.Name == Text + "a")
            {
                var href = (string?)el.Attribute(XLink + "href") ?? "";
                sb.Append($"<a href=\"{Escape(href)}\">{Escape(el.Value)}</a>");
            }
            else if (el.Name == Text + "s")
            {
                var count = (int?)el.Attribute(Text + "c") ?? 1;
                sb.Append(new string(' ', Math.Min(count, 64)));
            }
            else if (el.Name == Text + "tab")
            {
                sb.Append("&emsp;");
            }
            else if (el.Name == Text + "line-break")
            {
                sb.Append("<br/>");
            }
            else if (el.Name == Draw + "frame")
            {
                if (zip is not null) RenderFrame(el, sb, zip);
            }
            else if (el.Name == Text + "span")
            {
                sb.Append(RenderInline(el, zip));
            }
            else
            {
                sb.Append(Escape(el.Value));
            }
        }
        return sb.ToString();
    }

    private static void RenderFrame(XElement frame, StringBuilder sb, ZipArchive zip)
    {
        var image = frame.Element(Draw + "image");
        if (image is null) return;
        var href = (string?)image.Attribute(XLink + "href") ?? "";
        // href 形如 "Pictures/xxx.png"，去掉开头的 ./
        var entryName = href.TrimStart('.', '/').Replace('\\', '/');
        var imgEntry = zip.GetEntry(entryName);
        if (imgEntry is null) return;
        using var ms = new MemoryStream();
        using (var s = imgEntry.Open()) s.CopyTo(ms);
        var mime = MimeFromExtension(Path.GetExtension(entryName));
        sb.Append($"<img src=\"data:{mime};base64,{Convert.ToBase64String(ms.ToArray())}\"/>");
    }

    // ── ods：表格 ──

    private static void RenderSheet(XElement tableEl, StringBuilder sb)
    {
        var name = (string?)tableEl.Attribute(Table + "name") ?? "工作表";
        sb.Append($"<div class=\"sheet-title\">{Escape(name)}</div><table>");
        foreach (var row in tableEl.Elements(Table + "table-row"))
        {
            var repeated = (int?)row.Attribute(Table + "number-rows-repeated") ?? 1;
            if (repeated > 200) repeated = 1; // 大片空行截断
            for (int r = 0; r < repeated; r++)
            {
                sb.Append("<tr>");
                foreach (var cell in row.Elements(Table + "table-cell"))
                {
                    var span = (int?)cell.Attribute(Table + "number-columns-repeated") ?? 1;
                    if (span > 200) span = 1;
                    var text = string.Join("<br/>",
                        cell.Elements(Text + "p").Select(p => RenderInline(p, null)).Where(t => t.Length > 0));
                    for (int c = 0; c < span; c++)
                        sb.Append($"<td>{(text.Length == 0 ? "&nbsp;" : text)}</td>");
                }
                sb.Append("</tr>");
            }
        }
        sb.Append("</table>");
    }

    // ── odp：幻灯片 ──

    private static void RenderSlide(XElement page, StringBuilder sb, ZipArchive zip)
    {
        sb.Append("<div class=\"slide\">");
        // 递归渲染页面里的 frame（文本框与图片）
        foreach (var el in page.Descendants())
        {
            if (el.Name == Draw + "frame")
            {
                var textBox = el.Element(Draw + "text-box");
                if (textBox is not null)
                {
                    foreach (var p in textBox.Elements(Text + "p"))
                    {
                        var inner = RenderInline(p, zip);
                        if (inner.Length > 0) sb.Append($"<p>{inner}</p>");
                    }
                }
                RenderFrame(el, sb, zip);
            }
        }
        sb.Append("</div>");
    }

    private static string MimeFromExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".tif" or ".tiff" => "image/tiff",
        _ => "application/octet-stream"
    };

    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
