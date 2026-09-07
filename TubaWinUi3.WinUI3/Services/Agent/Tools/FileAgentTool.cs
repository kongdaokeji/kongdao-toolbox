using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 文件操作工具：列目录、读文件、写/追加/编辑、查找、删除、移动、复制。
/// 写类操作受 <see cref="FileSandbox"/> 保护并需用户确认。
/// </summary>
public static class FileAgentTool
{
    public static void Register()
    {
        Add("list_dir", "查看目录", "\uE838", false, (Func<string, string>)ListDir);
        Add("get_info", "查看文件信息", "\uE8E5", false, (Func<string, string>)GetInfo);
        Add("read_file", "读取文件", "\uE8E5", false, (Func<string, int?, string>)ReadFile);
        Add("write_file", "写入文件", "\uE8B3", true, (Func<string, string, string, string>)WriteFile, "write_file");
        Add("append_file", "追加文件", "\uE70F", true, (Func<string, string, string, string>)AppendFile, "write_file");
        Add("edit_file", "编辑文件", "\uE70F", true, (Func<string, string, string, string, string>)EditFile, "write_file");
        Add("find_files", "查找文件", "\uE721", false, (Func<string, string?, int?, string>)FindFiles);
        Add("delete_file", "删除文件", "\uE74D", true, (Func<string, string, string>)DeleteFile, "delete_file");
        Add("move_file", "移动文件", "\uE8AC", true, (Func<string, string, string, string>)MoveFile, "move_file");
        Add("copy_file", "复制文件", "\uE8C8", true, (Func<string, string, string, string>)CopyFile, "copy_file");
    }

    [Description("列出目录内容（文件和子目录，最多 200 项）")]
    public static string ListDir(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "错误：缺少 path 参数";

        if (!Directory.Exists(path))
            return $"错误：目录 '{path}' 不存在";

        var sb = new StringBuilder();
        sb.AppendLine($"目录内容：{path}");
        sb.AppendLine();

        try
        {
            var count = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                RecurseSubdirectories = false
            }))
            {
                if (count >= 200)
                {
                    sb.AppendLine("... (超过 200 项，已截断)");
                    break;
                }

                try
                {
                    if (Directory.Exists(entry))
                    {
                        var di = new DirectoryInfo(entry);
                        sb.AppendLine($"[目录] {di.Name}  修改: {di.LastWriteTime:yyyy-MM-dd}");
                    }
                    else
                    {
                        var fi = new FileInfo(entry);
                        sb.AppendLine($"[文件] {fi.Name}  大小: {AgentToolHelpers.FormatSize(fi.Length)}  修改: {fi.LastWriteTime:yyyy-MM-dd}");
                    }
                }
                catch
                {
                    sb.AppendLine($"[未知] {Path.GetFileName(entry)}");
                }
                count++;
            }

            if (count == 0) sb.AppendLine("(空目录)");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    [Description("获取文件或文件夹信息（大小、时间、属性）")]
    public static string GetInfo(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "错误：缺少 path 参数";

        var sb = new StringBuilder();

        try
        {
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                sb.AppendLine("类型：目录");
                sb.AppendLine($"路径：{di.FullName}");
                sb.AppendLine($"创建时间：{di.CreationTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"修改时间：{di.LastWriteTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"属性：{di.Attributes}");
            }
            else if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                sb.AppendLine("类型：文件");
                sb.AppendLine($"路径：{fi.FullName}");
                sb.AppendLine($"大小：{AgentToolHelpers.FormatSize(fi.Length)}");
                sb.AppendLine($"创建时间：{fi.CreationTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"修改时间：{fi.LastWriteTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"属性：{fi.Attributes}");
            }
            else
            {
                sb.AppendLine($"路径 '{path}' 不存在");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"获取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    [Description("读取文本文件内容（超过 5MB 或 20 万字符会被拒绝/截断）")]
    public static string ReadFile(string path, int? maxChars = null)
    {
        if (FileSandbox.ValidateRead(path) is { } readErr)
            return $"错误：{readErr}";

        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return $"错误：文件 '{path}' 不存在";
            if (fi.Length > FileSandbox.MaxReadFileBytes)
                return $"错误：文件过大（{AgentToolHelpers.FormatSize(fi.Length)}），超过读取上限 5MB，请用 get_info 查看基本信息";

            var text = File.ReadAllText(path);
            var limit = maxChars is > 0 ? Math.Min(maxChars.Value, FileSandbox.MaxReadChars) : FileSandbox.MaxReadChars;
            if (text.Length > limit)
                text = text[..limit] + $"\n…（内容过长已截断，全文 {fi.Length} 字节）";

            return $"文件：{path}\n大小：{AgentToolHelpers.FormatSize(fi.Length)}\n\n--- 内容开始 ---\n{text}\n--- 内容结束 ---";
        }
        catch (Exception ex)
        {
            return $"读取失败：{ex.Message}";
        }
    }

    [Description("写入文件（覆盖已有内容；受安全沙箱保护，需用户确认后执行）")]
    public static string WriteFile(string path, string content, string reason)
    {
        if (FileSandbox.ValidateWrite(path) is { } err) return $"错误：{err}";
        if (content.Length > FileSandbox.MaxWriteChars)
            return $"错误：内容过长（{content.Length} 字符），超过写入上限 {FileSandbox.MaxWriteChars}";

        try
        {
            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(full, content);
            return $"已写入文件：{full}（{content.Length} 字符）";
        }
        catch (Exception ex)
        {
            return $"写入失败：{ex.Message}";
        }
    }

    [Description("追加内容到文件末尾（不存在则创建；需用户确认后执行）")]
    public static string AppendFile(string path, string content, string reason)
    {
        if (FileSandbox.ValidateWrite(path) is { } err) return $"错误：{err}";
        if (content.Length > FileSandbox.MaxWriteChars)
            return $"错误：内容过长（{content.Length} 字符）";

        try
        {
            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(full, content);
            return $"已追加到文件：{full}";
        }
        catch (Exception ex)
        {
            return $"追加失败：{ex.Message}";
        }
    }

    [Description("编辑文件：把第一个出现的 oldText 替换为 newText（需用户确认后执行）。oldText 必须与文件原文完全一致（含空白与换行），建议先 read_file 确认原文")]
    public static string EditFile(string path, string oldText, string newText, string reason)
    {
        if (FileSandbox.ValidateWrite(path) is { } err) return $"错误：{err}";
        if (string.IsNullOrEmpty(oldText)) return "错误：oldText 不能为空";

        try
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full)) return $"错误：文件 '{full}' 不存在";

            var text = File.ReadAllText(full);
            var idx = text.IndexOf(oldText, StringComparison.Ordinal);
            if (idx < 0) return $"错误：在文件中未找到要替换的内容（oldText 与文件内容不完全匹配）";

            text = text[..idx] + newText + text[(idx + oldText.Length)..];
            File.WriteAllText(full, text);
            return $"已编辑文件：{full}（替换 {oldText.Length} 字符 → {newText.Length} 字符）";
        }
        catch (Exception ex)
        {
            return $"编辑失败：{ex.Message}";
        }
    }

    [Description("递归查找文件（按文件名通配符，如 *.txt；最多 50 个结果）")]
    public static string FindFiles(string path, string? pattern = null, int? maxResults = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return "错误：缺少 path 参数";
        if (!Directory.Exists(path)) return $"错误：目录 '{path}' 不存在";

        var sb = new StringBuilder();
        var limit = maxResults is > 0 ? Math.Min(maxResults.Value, 50) : 50;
        var count = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, pattern ?? "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                MaxRecursionDepth = 8
            }))
            {
                try
                {
                    var fi = new FileInfo(file);
                    sb.AppendLine($"{fi.FullName}  {AgentToolHelpers.FormatSize(fi.Length)}");
                    if (++count >= limit)
                    {
                        sb.AppendLine($"... (已找到 {count} 个，达到上限)");
                        return sb.ToString();
                    }
                }
                catch { }
            }
            sb.AppendLine(count == 0 ? "（未找到匹配文件）" : $"共找到 {count} 个文件");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"查找失败：{ex.Message}");
        }

        return sb.ToString();
    }

    [Description("删除文件（需用户确认后执行；只能删除文件，不能删除目录）")]
    public static string DeleteFile(string path, string reason)
    {
        if (FileSandbox.ValidateWrite(path) is { } err) return $"错误：{err}";

        try
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full)) return $"错误：文件 '{full}' 不存在";
            if (Directory.Exists(full)) return "错误：目标是一个目录，delete_file 只能删除文件";

            File.Delete(full);
            return $"已删除文件：{full}";
        }
        catch (Exception ex)
        {
            return $"删除失败：{ex.Message}";
        }
    }

    [Description("移动文件或重命名（需用户确认后执行）")]
    public static string MoveFile(string source, string destination, string reason)
    {
        if (FileSandbox.ValidateRead(source) is { } srcErr) return $"错误：{srcErr}";
        if (FileSandbox.ValidateWrite(destination) is { } dstErr) return $"错误：{dstErr}";

        try
        {
            var src = Path.GetFullPath(source);
            var dst = Path.GetFullPath(destination);
            if (!File.Exists(src)) return $"错误：源文件 '{src}' 不存在";

            var dir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.Move(src, dst, overwrite: true);
            return $"已移动：{src} → {dst}";
        }
        catch (Exception ex)
        {
            return $"移动失败：{ex.Message}";
        }
    }

    [Description("复制文件（需用户确认后执行）")]
    public static string CopyFile(string source, string destination, string reason)
    {
        if (FileSandbox.ValidateRead(source) is { } srcErr) return $"错误：{srcErr}";
        if (FileSandbox.ValidateWrite(destination) is { } dstErr) return $"错误：{dstErr}";

        try
        {
            var src = Path.GetFullPath(source);
            var dst = Path.GetFullPath(destination);
            if (!File.Exists(src)) return $"错误：源文件 '{src}' 不存在";

            var dir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.Copy(src, dst, overwrite: true);
            return $"已复制：{src} → {dst}";
        }
        catch (Exception ex)
        {
            return $"复制失败：{ex.Message}";
        }
    }

    private static void Add(string name, string displayName, string glyph, bool dangerous, Delegate method, string? confirmKind = null)
    {
        AgentToolRegistry.Register(new AgentTool
        {
            Name = name,
            DisplayName = displayName,
            Glyph = glyph,
            Function = AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = name }),
            RequiresConfirmation = dangerous,
            ConfirmKind = confirmKind,
        });
    }
}
