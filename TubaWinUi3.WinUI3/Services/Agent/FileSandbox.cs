namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 文件操作沙箱策略：写操作拒绝系统关键目录、应用自身目录与可执行/脚本文件类型。
/// 纯函数实现，便于单元测试。
/// </summary>
public static class FileSandbox
{
    public const int MaxReadFileBytes = 5 * 1024 * 1024;   // 读取上限 5MB
    public const int MaxReadChars = 200_000;               // 读取文本上限
    public const int MaxWriteChars = 200_000;              // 写入文本上限

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".drv", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".scr"
    };

    /// <summary>校验写路径；返回 null 表示允许，否则返回拒绝原因。</summary>
    public static string? ValidateWrite(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "路径不能为空";
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return "路径包含非法字符";

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) { return $"路径无效：{ex.Message}"; }

        if (IsProtectedRoot(full))
            return $"路径受安全沙箱保护，不允许写入系统关键目录或程序目录：{full}";

        var ext = Path.GetExtension(full);
        if (BlockedExtensions.Contains(ext))
            return $"安全策略不允许写入可执行/脚本文件（{ext}）";

        return null;
    }

    /// <summary>校验读路径存在性（只读操作无需沙箱限制）。</summary>
    public static string? ValidateRead(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "路径不能为空";
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return "路径包含非法字符";
        return null;
    }

    /// <summary>判断完整路径是否位于受保护根目录内。</summary>
    public static bool IsProtectedRoot(string fullPath)
    {
        var roots = new List<string>();

        var sysRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrEmpty(sysRoot)) roots.Add(sysRoot);

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(pf)) roots.Add(pf);

        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(pf86)) roots.Add(pf86);

        roots.Add(AppContext.BaseDirectory);

        try { roots.Add(ConfigManager.GetDataDir()); } catch { }

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            try
            {
                if (IsWithin(root, fullPath)) return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>判断 fullPath 是否位于 root 目录内（大小写不敏感）。</summary>
    public static bool IsWithin(string root, string fullPath)
    {
        var r = Path.GetFullPath(root).TrimEnd('\\') + "\\";
        var f = Path.GetFullPath(fullPath);
        return f.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }
}
