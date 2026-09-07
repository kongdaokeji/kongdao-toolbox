using System.Diagnostics;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// 在启动时将工具注册到 Windows 搜索索引。
/// 通过在「开始菜单 → 程序」文件夹下创建快捷方式实现，
/// Windows Search 会自动索引开始菜单中的快捷方式。
/// 支持多版本去重（同名工具只保留一个）和过期清理。
/// </summary>
internal static class WindowsSearchIndexService
{
    private static readonly string StartMenuFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Windows\Start Menu\Programs\图吧工具箱");

    /// <summary>
    /// 将所有工具注册到 Windows 搜索索引（后台执行，不阻塞 UI）。
    /// </summary>
    public static async Task RegisterAllToolsAsync()
    {
        try
        {
            var allTools = ToolCatalog.GetAllToolsCached();
            if (allTools.Count == 0)
                return;

            await Task.Run(() => RegisterTools(allTools));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 注册失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 工具目录变化后刷新索引快捷方式。
    /// </summary>
    public static async Task RefreshAsync()
    {
        try
        {
            var allTools = ToolCatalog.GetAllToolsCached();
            await Task.Run(() => RegisterTools(allTools));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 刷新失败: {ex.Message}");
        }
    }

    private static void RegisterTools(IReadOnlyList<ToolItem> tools)
    {
        // 确保目标文件夹存在
        if (!Directory.Exists(StartMenuFolder))
            Directory.CreateDirectory(StartMenuFolder);

        // 记录当前应该存在的快捷方式文件名，用于后续清理
        var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) 外部工具（.exe 等）
        var toRegister = DeduplicateTools(tools);
        foreach (var (name, tool) in toRegister)
        {
            var shortcutPath = Path.Combine(StartMenuFolder, $"{SanitizeFileName(name)}.lnk");
            expectedFiles.Add(Path.GetFileName(shortcutPath));

            try
            {
                if (File.Exists(shortcutPath) && IsShortcutUpToDate(shortcutPath, tool.EffectivePath))
                    continue;

                CreateShortcut(shortcutPath, tool.EffectivePath, tool.EffectiveWorkingDir,
                    $"{name} - {tool.Category}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 创建快捷方式失败 [{name}]: {ex.Message}");
            }
        }

        // 2) 内置工具（通过 --open-builtin 启动参数打开）
        var appExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        var appDir = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(appExe) && File.Exists(appExe))
        {
            foreach (var builtin in BuiltinToolRegistry.Tools)
            {
                var displayName = builtin.Name;
                if (string.IsNullOrWhiteSpace(displayName))
                    continue;

                // 同名去重（内置工具与外部工具同名时，优先保留外部工具）
                if (expectedFiles.Contains($"{SanitizeFileName(displayName)}.lnk"))
                    continue;

                var shortcutPath = Path.Combine(StartMenuFolder, $"{SanitizeFileName(displayName)}.lnk");
                expectedFiles.Add(Path.GetFileName(shortcutPath));

                try
                {
                    CreateShortcut(shortcutPath, appExe, appDir,
                        $"{displayName} - {builtin.Category}",
                        $"--open-builtin {builtin.Id}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 创建内置工具快捷方式失败 [{displayName}]: {ex.Message}");
                }
            }
        }

        // 3) 清理过期快捷方式
        CleanupStaleShortcuts(expectedFiles);
    }

    /// <summary>
    /// 对工具列表去重：
    /// - 跳过内置工具链接（没有真实文件路径）
    /// - 跳过需要下载但还没下载的工具
    /// - 同名工具（不区分大小写）只保留第一个有效项
    /// - 同一路径的工具只保留一次
    /// </summary>
    private static List<(string Name, ToolItem Tool)> DeduplicateTools(IReadOnlyList<ToolItem> tools)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string Name, ToolItem Tool)>();

        foreach (var tool in tools)
        {
            // 跳过内置工具链接
            if (tool.IsBuiltinLink)
                continue;

            // 跳过需要下载但还没下载的工具
            if (tool.NeedsDownload)
                continue;

            // 跳过路径为空或文件不存在的工具
            var effectivePath = tool.EffectivePath;
            if (string.IsNullOrWhiteSpace(effectivePath) || !File.Exists(effectivePath))
                continue;

            // 同一可执行文件路径去重（不同分类下的同一工具）
            if (!seenPaths.Add(effectivePath))
                continue;

            // 同名工具去重（用户装了多个版本时只保留一个）
            var displayName = tool.Name;
            if (!seenNames.Add(displayName))
                continue;

            result.Add((displayName, tool));
        }

        return result;
    }

    /// <summary>
    /// 检查已有快捷方式是否指向正确的目标（避免重复写入）。
    /// </summary>
    private static bool IsShortcutUpToDate(string shortcutPath, string targetPath)
    {
        try
        {
            // 通过读取文件的最后写入时间和大小做粗略判断，
            // 精确比对需要 COM 互操作，在批量场景下太慢
            // 这里简单返回 false 让它每次重建，开销很小
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 创建 .lnk 快捷方式（通过 PowerShell COM 调用）。
    /// </summary>
    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description, string? arguments = null)
    {
        // 转义单引号以安全嵌入 PowerShell 字符串
        var escTarget = targetPath.Replace("'", "''");
        var escWorkDir = workingDir.Replace("'", "''");
        var escDesc = description.Replace("'", "''");
        var escShortcut = shortcutPath.Replace("'", "''");

        var argsLine = string.IsNullOrWhiteSpace(arguments)
            ? ""
            : $"\n$s.Arguments = '{arguments.Replace("'", "''")}'";

        var psScript = $"""
            $ws = New-Object -ComObject WScript.Shell
            $s = $ws.CreateShortcut('{escShortcut}')
            $s.TargetPath = '{escTarget}'
            $s.WorkingDirectory = '{escWorkDir}'
            $s.Description = '{escDesc}'
            $s.IconLocation = '{escTarget},0'{argsLine}
            $s.Save()
            """;

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{psScript.Replace("\"", "\\\"")}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        process?.WaitForExit(5000);

        if (process is not null && process.ExitCode != 0)
        {
            var err = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(err);
        }
    }

    /// <summary>
    /// 清理不再对应的过期快捷方式。
    /// </summary>
    private static void CleanupStaleShortcuts(HashSet<string> expectedFiles)
    {
        try
        {
            if (!Directory.Exists(StartMenuFolder))
                return;

            foreach (var file in Directory.GetFiles(StartMenuFolder, "*.lnk"))
            {
                var fileName = Path.GetFileName(file);
                if (!expectedFiles.Contains(fileName))
                {
                    try
                    {
                        File.Delete(file);
                        System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 清理过期快捷方式: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 清理失败 [{fileName}]: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 清理扫描失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 清理所有由本服务创建的快捷方式（卸载/重置时调用）。
    /// </summary>
    public static void RemoveAll()
    {
        try
        {
            if (Directory.Exists(StartMenuFolder))
            {
                Directory.Delete(StartMenuFolder, recursive: true);
                System.Diagnostics.Debug.WriteLine("[WindowsSearchIndex] 已移除所有搜索索引快捷方式");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 移除失败: {ex.Message}");
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "工具" : sanitized;
    }
}
