using System.IO.Compression;
using System.Net;
using System.Xml.Linq;

namespace TubaWinUi3.Services;

/// <summary>
/// PPTX 轻量渲染：直接解包 pptx（zip），从每张幻灯片的 XML 中提取
/// 文本（a:t）与内嵌图片（a:blip → media），生成可打印的 HTML。
/// 内容保真、排版简化（纯逻辑，可单元测试）。
/// </summary>
public static class PptxToHtmlConverter
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static string ToHtml(string pptxPath)
    {
        using var zip = ZipFile.OpenRead(pptxPath);
        return ToHtml(zip);
    }

    public static string ToHtml(ZipArchive zip)
    {
        var slides = zip.Entries
            .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                && !e.FullName.Contains("/_rels/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => ParseSlideNumber(e.FullName))
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.Append("<div class=\"pptx\">");
        foreach (var slide in slides)
        {
            sb.Append(RenderSlide(zip, slide));
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private static int ParseSlideNumber(string fullName)
    {
        var name = Path.GetFileNameWithoutExtension(fullName);
        return name.Length > 5 && int.TryParse(name.AsSpan(5), out var n) ? n : int.MaxValue;
    }

    private static string RenderSlide(ZipArchive zip, ZipArchiveEntry slideEntry)
    {
        XDocument doc;
        try
        {
            using var s = slideEntry.Open();
            doc = XDocument.Load(s);
        }
        catch
        {
            // 单页损坏不阻断整份文档
            return "<section class=\"slide\"><p>（此页无法解析）</p></section>";
        }

        var relsPath = $"ppt/slides/_rels/{slideEntry.Name}.rels";
        var imageTargets = LoadImageTargets(zip, relsPath);

        var sb = new System.Text.StringBuilder();
        sb.Append("<section class=\"slide\">");
        sb.Append($"<div class=\"slide-num\">{slideEntry.Name.Replace("slide", "第 ").Replace(".xml", " 页")}</div>");

        // 形状：文本框（文本）与图片
        foreach (var sp in doc.Descendants(P + "sp"))
        {
            RenderTextBox(sp, sb);
        }
        foreach (var pic in doc.Descendants(P + "pic"))
        {
            RenderPicture(pic, zip, imageTargets, sb);
        }

        sb.Append("</section>");
        return sb.ToString();
    }

    private static Dictionary<string, string> LoadImageTargets(ZipArchive zip, string relsPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entry = zip.GetEntry(relsPath);
        if (entry is null) return map;

        XDocument doc;
        try
        {
            using var s = entry.Open();
            doc = XDocument.Load(s);
        }
        catch
        {
            return map;
        }

        foreach (var rel in doc.Root?.Elements(Rel + "Relationship") ?? Enumerable.Empty<XElement>())
        {
            var id = (string?)rel.Attribute("Id");
            var target = (string?)rel.Attribute("Target");
            var type = (string?)rel.Attribute("Type") ?? "";
            if (id is null || target is null) continue;
            if (!type.EndsWith("/image", StringComparison.OrdinalIgnoreCase)) continue;

            // Target 相对 slides 目录（如 ../media/image1.png），规范化为 zip 内路径
            var normalized = NormalizeZipPath($"ppt/slides/{target}");
            map[id] = normalized;
        }
        return map;
    }

    private static string NormalizeZipPath(string path)
    {
        var parts = new List<string>();
        foreach (var seg in path.Replace('\\', '/').Split('/'))
        {
            if (seg == "" || seg == ".") continue;
            if (seg == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(seg);
        }
        return string.Join("/", parts);
    }

    private static void RenderTextBox(XElement sp, System.Text.StringBuilder sb)
    {
        var txBody = sp.Element(P + "txBody");
        if (txBody is null) return;

        var isTitle = sp.Descendants(P + "ph").Any(ph => (string?)ph.Attribute("type") == "title");
        // 注意：文本段落是 DrawingML 命名空间的 a:p（不是 p:p）
        foreach (var para in txBody.Elements(A + "p"))
        {
            var text = string.Concat(para.Descendants(A + "t").Select(t => t.Value));
            if (string.IsNullOrWhiteSpace(text)) continue;
            var cls = isTitle ? " class=\"slide-title\"" : "";
            sb.Append($"<p{cls}>{WebUtility.HtmlEncode(text)}</p>");
        }
    }

    private static void RenderPicture(XElement pic, ZipArchive zip, Dictionary<string, string> imageTargets, System.Text.StringBuilder sb)
    {
        foreach (var blip in pic.Descendants(A + "blip"))
        {
            var embed = (string?)blip.Attribute(R + "embed");
            if (embed is null || !imageTargets.TryGetValue(embed, out var target)) continue;
            var entry = zip.GetEntry(target);
            if (entry is null) continue;

            byte[] bytes;
            using (var s = entry.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                bytes = ms.ToArray();
            }

            var mime = GetMime(Path.GetExtension(target));
            sb.Append($"<img src=\"data:{mime};base64,{Convert.ToBase64String(bytes)}\" alt=\"\">");
        }
    }

    private static string GetMime(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".tiff" or ".tif" => "image/tiff",
        _ => "image/png"
    };
}