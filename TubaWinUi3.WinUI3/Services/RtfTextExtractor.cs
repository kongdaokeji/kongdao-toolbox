using System.Text;

namespace TubaWinUi3.Services;

/// <summary>
/// 轻量 RTF 文本提取器：处理 \par / \line 换行、\'hh 十六进制（按 GBK 双字节配对）、
/// \uN Unicode、跳过字体表 / 样式表 / 图片等目的地组与 \bin 二进制块。
/// 只提取纯文本，不保留格式。
/// </summary>
public static class RtfTextExtractor
{
    static RtfTextExtractor() => Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    /// <summary>\* 或已知目的地控制字开头的组不输出（字体表、颜色表、图片等）。</summary>
    private static readonly HashSet<string> Destinations = new(StringComparer.OrdinalIgnoreCase)
    {
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "object", "header", "footer",
        "headerl", "headerr", "headerf", "footerl", "footerr", "footerf", "footnote",
        "listtable", "listoverridetable", "rsidtbl", "generator", "themedata",
        "colorschememapping", "datastore", "latentstyles", "mmath", "field", "shpinst",
        "nonshppict", "panose", "fname", "falt", "romfill", "rommark"
    };

    public static string Extract(string rtf)
    {
        var sb = new StringBuilder();
        var outputStack = new Stack<bool>();
        bool output = true;
        var pendingHex = new List<byte>(); // 连续 \'hh 转义累积（用于 GBK 双字节配对）
        int i = 0;
        int len = rtf?.Length ?? 0;

        void FlushHex()
        {
            if (pendingHex.Count == 0) return;
            if (output)
                sb.Append(DecodeGbk(pendingHex.ToArray()));
            pendingHex.Clear();
        }

        while (i < len)
        {
            var c = rtf![i];
            switch (c)
            {
                case '{':
                    FlushHex();
                    outputStack.Push(output);
                    // 检查本组是否为目的地组：{ \* ... } 或 { \fonttbl ... }
                    var j = i + 1;
                    bool starred = false, dest = false;
                    while (j < len && rtf[j] == ' ') j++;
                    if (j < len && rtf[j] == '\\')
                    {
                        j++;
                        if (j < len && rtf[j] == '*') { starred = true; j++; }
                        var wordStart = j;
                        while (j < len && char.IsLetter(rtf[j])) j++;
                        var word = rtf[wordStart..j];
                        if (starred || Destinations.Contains(word)) dest = true;
                    }
                    if (dest) output = false;
                    i++;
                    break;

                case '}':
                    FlushHex();
                    output = outputStack.Count > 0 ? outputStack.Pop() : output;
                    i++;
                    break;

                case '\\':
                {
                    // \'hh 连续转义用于 GBK 双字节配对，遇到下一个 \' 不打断累积
                    if (!(i + 1 < len && rtf[i + 1] == '\'')) FlushHex();
                    i++;
                    if (i >= len) break;
                    var nc = rtf[i];
                    switch (nc)
                    {
                        case '\\' when output: sb.Append('\\'); i++; break;
                        case '{' when output: sb.Append('{'); i++; break;
                        case '}' when output: sb.Append('}'); i++; break;
                        case '\'': // \'hh
                        {
                            i++;
                            if (i + 1 < len)
                            {
                                if (TryHex(rtf[i], rtf[i + 1], out var b))
                                {
                                    pendingHex.Add(b);
                                    i += 2;
                                }
                            }
                            else i = len;
                            break;
                        }
                        case 'u': // \uNNNN 后跟一个替代字符（可能为 \ucN 控制）
                        {
                            i++;
                            var start = i;
                            if (i < len && (rtf[i] == '-' || char.IsDigit(rtf[i])))
                            {
                                while (i < len && char.IsDigit(rtf[i])) i++;
                                if (int.TryParse(rtf[start..i], out var code))
                                {
                                    if (code < 0) code += 65536;
                                    if (output)
                                        sb.Append(char.ConvertFromUtf32(code));
                                }
                            }
                            else // \ucN / \up 等其他 u 开头控制字：按一般控制字跳过
                            {
                                while (i < len && char.IsLetter(rtf[i])) i++;
                            }
                            // 跳过随后的替代字符：一个 '?' 或一个 \'hh 转义
                            if (i < len && rtf[i] == '?') i++;
                            else if (i + 3 < len && rtf[i] == '\\' && rtf[i + 1] == '\'') i += 4;
                            break;
                        }
                        default:
                        {
                            if (char.IsLetter(nc))
                            {
                                var start = i;
                                while (i < len && char.IsLetter(rtf[i])) i++;
                                var word = rtf[start..i];
                                // 数字参数（可为负）
                                if (i < len && (rtf[i] == '-' || char.IsDigit(rtf[i])))
                                {
                                    var ps = i;
                                    if (rtf[i] == '-') i++;
                                    while (i < len && char.IsDigit(rtf[i])) i++;
                                    if (long.TryParse(rtf[ps..i], out var num) && word == "bin" && num > 0)
                                        i += (int)Math.Min(num, len - i); // 跳过二进制数据
                                }
                                if (i < len && rtf[i] == ' ') i++; // 参数后的分隔空格
                                if (output)
                                {
                                    if (word is "par" or "line" or "page" or "sect") sb.Append('\n');
                                    else if (word is "tab" or "cell") sb.Append(word == "tab" ? '\t' : '\t');
                                    else if (word == "emdash") sb.Append("—");
                                    else if (word == "endash") sb.Append("–");
                                    else if (word == "lquote") sb.Append("‘");
                                    else if (word == "rquote") sb.Append("’");
                                    else if (word == "ldblquote") sb.Append("“");
                                    else if (word == "rdblquote") sb.Append("”");
                                    else if (word == "bullet") sb.Append("•");
                                }
                            }
                            else i++; // \~ 等符号控制：忽略（\~ 为不换行空格，输出空格）
                            break;
                        }
                    }
                    break;
                }

                case '\n' or '\r':
                    FlushHex();
                    i++; // RTF 源码换行不是内容
                    break;

                default:
                    FlushHex();
                    if (output) sb.Append(c);
                    i++;
                    break;
            }
        }
        FlushHex();

        return sb.ToString()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n\n\n", "\n\n", StringComparison.Ordinal) // 压缩过多空行
            .Trim();
    }

    private static bool TryHex(char h, char l, out byte value)
    {
        value = 0;
        if (!Uri.IsHexDigit(h) || !Uri.IsHexDigit(l)) return false;
        value = Convert.ToByte($"{h}{l}", 16);
        return true;
    }

    private static string DecodeGbk(byte[] bytes)
    {
        try
        {
            var enc = Encoding.GetEncoding(936);
            return enc.GetString(bytes);
        }
        catch
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
