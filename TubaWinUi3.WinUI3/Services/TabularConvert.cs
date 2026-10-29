using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TubaWinUi3.Services;

/// <summary>
/// 文本类数据互转：CSV / JSON / Markdown / HTML（含智能读取本地文本的编码探测）。
/// 纯逻辑，可单元测试。
/// </summary>
public static class TabularConvert
{
    static TabularConvert() => Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    /// <summary>中文等非 ASCII 字符不转义为 \uXXXX（输出可读）。</summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions JsonOptsIndented = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    // ── 智能读文本（BOM → UTF-8 严格 → GB18030 回退） ──

    /// <summary>读取本地文本文件：识别 BOM，UTF-8 校验失败时按 GB18030 解码（中文 txt 常见）。</summary>
    public static string ReadTextSmart(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0) return "";
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
    }

    // ── CSV ──

    /// <summary>解析 CSV（支持引号包裹、引号转义 ""、CRLF）。</summary>
    public static List<string[]> ParseCsv(string csv)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        int i = 0;
        bool inQuotes = false;
        bool cellStarted = false;

        void EndCell()
        {
            row.Add(cell.ToString());
            cell.Clear();
            cellStarted = false;
        }
        void EndRow()
        {
            EndCell();
            // 丢弃完全空白的尾行
            if (row.Count > 1 || row[0].Length > 0)
                rows.Add(row.ToArray());
            row.Clear();
        }

        while (i < csv.Length)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i += 2; }
                    else { inQuotes = false; i++; }
                }
                else { cell.Append(c); i++; }
                continue;
            }
            switch (c)
            {
                case '"':
                    inQuotes = true;
                    cellStarted = true;
                    i++;
                    break;
                case ',':
                    EndCell();
                    i++;
                    break;
                case '\r':
                    if (i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    EndRow();
                    i++;
                    break;
                case '\n':
                    EndRow();
                    i++;
                    break;
                default:
                    cell.Append(c);
                    cellStarted = true;
                    i++;
                    break;
            }
        }
        if (cell.Length > 0 || cellStarted || row.Count > 0) EndRow();
        return rows;
    }

    /// <summary>写出 CSV（含逗号/引号/换行的字段自动加引号转义）。</summary>
    public static string WriteCsv(IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(EscapeCsvField)));
        return sb.ToString();
    }

    private static string EscapeCsvField(string? field)
    {
        field ??= "";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return field;
    }

    // ── CSV ↔ JSON ──

    /// <summary>CSV → JSON：首行作表头，输出对象数组（不足的列补空字符串）。</summary>
    public static string CsvToJson(string csv)
    {
        var rows = ParseCsv(csv);
        if (rows.Count == 0) return "[]";
        var header = rows[0];
        var sb = new StringBuilder("[\n");
        for (int r = 1; r < rows.Count; r++)
        {
            sb.Append("  {");
            for (int c = 0; c < header.Length; c++)
            {
                if (c > 0) sb.Append(", ");
                var value = c < rows[r].Length ? rows[r][c] : "";
                sb.Append(JsonSerializer.Serialize(header[c], JsonOpts) + ": " + JsonSerializer.Serialize(value, JsonOpts));
            }
            sb.Append("}" + (r < rows.Count - 1 ? "," : "") + "\n");
        }
        sb.Append("]");
        return sb.ToString();
    }

    /// <summary>JSON → CSV：要求顶层数组且元素为对象（嵌套值序列化为 JSON 文本）。</summary>
    public static string JsonToCsv(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("JSON → CSV 需要顶层数组（元素为对象）");
        var headers = new List<string>();
        var seen = new HashSet<string>();
        var records = new List<Dictionary<string, string>>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("JSON → CSV 需要顶层数组（元素为对象）");
            var record = new Dictionary<string, string>();
            foreach (var prop in item.EnumerateObject())
            {
                if (seen.Add(prop.Name)) headers.Add(prop.Name);
                record[prop.Name] = JsonValueToString(prop.Value);
            }
            records.Add(record);
        }
        if (headers.Count == 0) return "";
        var rows = new List<string[]> { headers.ToArray() };
        rows.AddRange(records.Select(r => headers.Select(h => r.TryGetValue(h, out var v) ? v : "").ToArray()));
        return WriteCsv(rows);
    }

    private static string JsonValueToString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? "",
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => el.GetRawText(),
        JsonValueKind.Null => "",
        _ => el.GetRawText()
    };

    // ── Markdown / HTML 输出 ──

    /// <summary>行集合 → Markdown 表格。</summary>
    public static string RowsToMarkdown(IEnumerable<string[]> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0) return "";
        var width = Math.Max(1, list.Max(r => r.Length));
        var sb = new StringBuilder();
        for (int r = 0; r < list.Count; r++)
        {
            sb.Append("| ");
            for (int c = 0; c < width; c++)
            {
                if (c > 0) sb.Append(" | ");
                sb.Append(c < list[r].Length ? EscapeMdCell(list[r][c]) : "");
            }
            sb.Append(" |\n");
            if (r == 0)
                sb.Append("| " + string.Join(" | ", Enumerable.Repeat("---", width)) + " |\n");
        }
        return sb.ToString();
    }

    private static string EscapeMdCell(string s)
        => s.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    /// <summary>CSV → Markdown（首行表头）。</summary>
    public static string CsvToMarkdown(string csv) => RowsToMarkdown(ParseCsv(csv));

    /// <summary>CSV → HTML 表格文档。</summary>
    public static string CsvToHtml(string csv, string? title = null)
    {
        var rows = ParseCsv(csv);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
        sb.Append($"<title>{HtmlConvert.Escape(title ?? "表格")}</title><style>");
        sb.Append("body{font-family:'Microsoft YaHei','Segoe UI',sans-serif;margin:24px;color:#1b1b1b;}");
        sb.Append("table{border-collapse:collapse;margin:12px 0;}th,td{border:1px solid #ccc;padding:5px 10px;font-size:13px;}");
        sb.Append("th{background:#f3f4f6;}tr:first-child td{background:#f3f4f6;font-weight:bold;}");
        sb.Append("</style></head><body>");
        if (!string.IsNullOrEmpty(title)) sb.Append($"<h2>{HtmlConvert.Escape(title)}</h2>");
        sb.Append("<table>");
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            foreach (var cell in row)
                sb.Append("<td>" + HtmlConvert.Escape(cell) + "</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    /// <summary>JSON → Markdown：数组对象渲染为表格，其他渲染为代码块。</summary>
    public static string JsonToMarkdown(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
            && root[0].ValueKind == JsonValueKind.Object)
        {
            var csv = JsonToCsv(json);
            return RowsToMarkdown(ParseCsv(csv));
        }
        return "```json\n" + PrettyJson(json) + "\n```\n";
    }

    /// <summary>JSON → HTML 文档（代码块展示，关键字着色可后续增强）。</summary>
    public static string JsonToHtml(string json)
    {
        var pretty = PrettyJson(json);
        return BuildHtmlDocument($"<pre><code>{HtmlConvert.Escape(pretty)}</code></pre>", "JSON");
    }

    /// <summary>JSON 美化（非法 JSON 时原样返回）。</summary>
    public static string PrettyJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, JsonOptsIndented);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>包装为完整 HTML 文档（带 charset 与基础排版样式，供打印 PDF / 另存 .html）。</summary>
    public static string BuildHtmlDocument(string bodyHtml, string? title = null)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
        sb.Append($"<title>{HtmlConvert.Escape(title ?? "文档")}</title><style>");
        sb.Append("body{font-family:'Microsoft YaHei','Segoe UI',sans-serif;margin:24px;color:#1b1b1b;line-height:1.6;}");
        sb.Append("h1,h2,h3{line-height:1.3;}table{border-collapse:collapse;margin:12px 0;}");
        sb.Append("th,td{border:1px solid #ccc;padding:5px 10px;}th{background:#f3f4f6;}");
        sb.Append("pre{background:#f6f8fa;border:1px solid #e1e4e8;border-radius:6px;padding:10px 12px;overflow:hidden;}");
        sb.Append("code{font-family:Consolas,monospace;}img{max-width:100%;}");
        sb.Append("</style></head><body>");
        if (!string.IsNullOrEmpty(title)) sb.Append($"<h1>{HtmlConvert.Escape(title)}</h1>");
        sb.Append(bodyHtml);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>纯文本 → HTML 段落文档。</summary>
    public static string TextToHtml(string text, string? title = null)
    {
        var sb = new StringBuilder();
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Trim().Length == 0) continue;
            // 连续两个以上空格/制表符视作代码缩进，保留为 pre
            if (line.StartsWith("  ") || line.StartsWith("\t"))
                sb.Append("<pre>" + HtmlConvert.Escape(line) + "</pre>");
            else
                sb.Append("<p>" + HtmlConvert.Escape(line) + "</p>");
        }
        return BuildHtmlDocument(sb.ToString(), title);
    }
}
