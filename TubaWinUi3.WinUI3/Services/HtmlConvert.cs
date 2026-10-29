using System.Text;
using HtmlAgilityPack;

namespace TubaWinUi3.Services;

/// <summary>
/// HTML → 纯文本 / Markdown（HtmlAgilityPack 遍历，覆盖常见块级与行内元素）。
/// 纯逻辑，可单元测试。
/// </summary>
public static class HtmlConvert
{
    /// <summary>HTML → 纯文本：块级元素换行，&lt;br&gt; 换行，解码实体。</summary>
    public static string ToPlainText(string html)
    {
        var doc = Load(html);
        var sb = new StringBuilder();
        RenderBlocks(doc.DocumentNode, sb, markdown: false);
        return Compact(sb.ToString());
    }

    /// <summary>HTML → Markdown：标题/列表/粗斜体/代码/链接/表格尽力映射。</summary>
    public static string ToMarkdown(string html)
    {
        var doc = Load(html);
        var sb = new StringBuilder();
        RenderBlocks(doc.DocumentNode, sb, markdown: true);
        return Compact(sb.ToString());
    }

    /// <summary>HTML 文本转义（供生成 HTML 的转换器使用）。</summary>
    public static string Escape(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static HtmlDocument Load(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }

    private static string Compact(string s)
    {
        var normalized = s.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\u00a0", " ", StringComparison.Ordinal);
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, "[ \t]+\n", "\n");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, "\n{3,}", "\n\n");
        return normalized.Trim('\n', ' ', '\t');
    }

    /// <summary>把节点下的块级内容渲染进 sb。</summary>
    private static void RenderBlocks(HtmlNode container, StringBuilder sb, bool markdown)
    {
        foreach (var node in container.ChildNodes)
            RenderNode(node, sb, markdown);
    }

    private static void RenderNode(HtmlNode node, StringBuilder sb, bool markdown)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Text:
                sb.Append(NormalizeSpace(HtmlEntity.DeEntitize(node.InnerText)));
                return;
            case HtmlNodeType.Comment:
                return;
        }

        var name = node.Name.ToLowerInvariant();
        switch (name)
        {
            case "script" or "style" or "head" or "noscript":
                return;
            case "br":
                sb.Append('\n');
                return;
            case "hr":
                EnsureNewline(sb);
                if (markdown) sb.Append("---\n");
                return;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
            {
                EnsureNewline(sb);
                var level = name[1] - '0';
                var text = InlineText(node, markdown).Trim();
                if (text.Length > 0)
                {
                    if (markdown) sb.Append(new string('#', level) + " " + text + "\n\n");
                    else sb.Append(text + "\n");
                }
                return;
            }
            case "p" or "div" or "section" or "article" or "header" or "footer" or "main" or "aside" or "figcaption" or "figure" or "center":
            {
                // 含块级子元素时递归渲染，否则按行内文本输出一段
                if (node.ChildNodes.Any(IsBlock))
                {
                    RenderBlocks(node, sb, markdown);
                    return;
                }
                var text = InlineText(node, markdown).Trim();
                if (text.Length == 0) return;
                EnsureNewline(sb);
                sb.Append(text + "\n\n");
                return;
            }
            case "pre":
            {
                EnsureNewline(sb);
                var text = node.InnerText.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
                if (markdown)
                    sb.Append("```\n" + text + "\n```\n\n");
                else
                    sb.Append(text + "\n\n");
                return;
            }
            case "blockquote":
            {
                EnsureNewline(sb);
                var inner = new StringBuilder();
                RenderBlocks(node, inner, markdown);
                var lines = inner.ToString().Trim().Split('\n');
                if (markdown)
                    foreach (var line in lines) sb.Append("> " + line.TrimEnd() + "\n");
                else
                    foreach (var line in lines) sb.Append(line.TrimEnd() + "\n");
                sb.Append('\n');
                return;
            }
            case "ul" or "ol":
            {
                EnsureNewline(sb);
                int index = 1;
                foreach (var li in node.Elements("li"))
                {
                    // 列表项：有块级子内容时递归缩进，否则取行内文本
                    string text;
                    if (li.ChildNodes.Any(IsBlock))
                    {
                        var inner = new StringBuilder();
                        RenderBlocks(li, inner, markdown);
                        text = inner.ToString().Trim().Replace("\n", " ", StringComparison.Ordinal);
                    }
                    else
                    {
                        text = InlineText(li, markdown).Trim();
                    }
                    if (markdown)
                        sb.Append(name == "ol" ? $"{index++}. {text}\n" : $"- {text}\n");
                    else
                        sb.Append(text + "\n");
                }
                sb.Append('\n');
                return;
            }
            case "table":
            {
                EnsureNewline(sb);
                var rows = new List<string[]>();
                foreach (var tr in node.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
                {
                    var cells = tr.Elements("td").Concat(tr.Elements("th"))
                        .Select(td => InlineText(td, markdown).Trim().Replace("\n", " ", StringComparison.Ordinal))
                        .ToArray();
                    if (cells.Length > 0) rows.Add(cells);
                }
                if (rows.Count > 0)
                {
                    var width = Math.Max(1, rows.Max(r => r.Length));
                    for (int r = 0; r < rows.Count; r++)
                    {
                        sb.Append(markdown
                            ? "| " + string.Join(" | ", PadRow(rows[r], width)) + " |\n"
                            : string.Join("\t", PadRow(rows[r], width)) + "\n");
                        // Markdown 表头分隔线插在首行之后
                        if (markdown && r == 0)
                            sb.Append("| " + string.Join(" | ", Enumerable.Repeat("---", width)) + " |\n");
                    }
                }
                sb.Append('\n');
                return;
            }
            case "img":
                if (markdown)
                {
                    var alt = node.GetAttributeValue("alt", "");
                    var src = node.GetAttributeValue("src", "");
                    sb.Append($"![{alt}]({src})");
                }
                return;
            default:
                // 行内容器或其他未知元素：直接下钻
                RenderBlocks(node, sb, markdown);
                return;
        }
    }

    /// <summary>渲染行内内容（粗体/斜体/代码/链接），忽略块级子节点。</summary>
    private static string InlineText(HtmlNode node, bool markdown)
    {
        var sb = new StringBuilder();
        void Walk(HtmlNode n)
        {
            if (n.NodeType == HtmlNodeType.Text)
            {
                sb.Append(NormalizeSpace(HtmlEntity.DeEntitize(n.InnerText)));
                return;
            }
            if (n.NodeType == HtmlNodeType.Comment) return;
            var inner = n.Name.ToLowerInvariant();
            switch (inner)
            {
                case "br": sb.Append('\n'); return;
                case "script" or "style": return;
                case "strong" or "b":
                    if (markdown) sb.Append("**");
                    WalkChildren(n);
                    if (markdown) sb.Append("**");
                    return;
                case "em" or "i":
                    if (markdown) sb.Append("*");
                    WalkChildren(n);
                    if (markdown) sb.Append("*");
                    return;
                case "code":
                    if (markdown) sb.Append("`");
                    sb.Append(NormalizeSpace(HtmlEntity.DeEntitize(n.InnerText)));
                    if (markdown) sb.Append("`");
                    return;
                case "a":
                {
                    var href = n.GetAttributeValue("href", "");
                    var text = n.InnerText.Trim();
                    if (markdown && !string.IsNullOrEmpty(href) && text.Length > 0)
                        sb.Append($"[{text}]({href})");
                    else
                        sb.Append(text);
                    return;
                }
                case "img":
                {
                    var alt = n.GetAttributeValue("alt", "");
                    var src = n.GetAttributeValue("src", "");
                    sb.Append(markdown ? $"![{alt}]({src})" : alt);
                    return;
                }
                default:
                    WalkChildren(n);
                    return;
            }
        }
        void WalkChildren(HtmlNode n)
        {
            foreach (var child in n.ChildNodes) Walk(child);
        }
        foreach (var child in node.ChildNodes)
            Walk(child);
        return sb.ToString();
    }

    private static string[] PadRow(string[] row, int width)
    {
        if (row.Length == width) return row;
        var padded = new string[width];
        for (int i = 0; i < width; i++)
            padded[i] = i < row.Length ? row[i].Replace("|", "\\|", StringComparison.Ordinal) : "";
        return padded;
    }

    private static bool IsBlock(HtmlNode node)
        => node.NodeType == HtmlNodeType.Element && BlockNames.Contains(node.Name.ToLowerInvariant());

    private static readonly HashSet<string> BlockNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "table", "tr", "td", "th",
        "pre", "blockquote", "hr", "br", "section", "article", "header", "footer", "main", "aside",
        "figure", "figcaption", "dl", "dt", "dd", "address"
    };

    private static void EnsureNewline(StringBuilder sb)
    {
        if (sb.Length == 0) return;
        var last = sb[sb.Length - 1];
        if (last != '\n') sb.Append('\n');
    }

    /// <summary>压缩空白：连续空格/制表符折叠为一个空格，行首行尾去除。</summary>
    private static string NormalizeSpace(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var trimmed = text.Trim('\r', '\n');
        var sb = new StringBuilder(trimmed.Length);
        bool pendingSpace = false;
        foreach (var ch in trimmed)
        {
            if (ch is ' ' or '\t' or '\u00a0')
                pendingSpace = true;
            else
            {
                if (pendingSpace && sb.Length > 0 && sb[^1] != ' ' && sb[^1] != '\n')
                    sb.Append(' ');
                pendingSpace = false;
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }
}
