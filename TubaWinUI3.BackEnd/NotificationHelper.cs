using Microsoft.Win32;

namespace TubaWinUI3.BackEnd;

/// <summary>
/// 主动拦截后端通知：双通道确保通知一定弹出。
/// 通道 A：写通知文件 → 主程序 FileSystemWatcher 监控到后弹 Toast（主程序运行时）。
/// 通道 B：后端启动主程序 --toast 模式 → 主程序弹 Toast 后退出（主程序未运行时）。
/// 两种通道的 Toast 点击后都会通过 COM 服务器启动主程序跳转审核页。
/// </summary>
public static class NotificationHelper
{
    // 后端 COM GUID（用于注册表注册）
    private const string ComGuid = "{A7F3B8E2-4D5C-4E6F-9A1B-2C3D4E5F6A7B}";

    // AppUserModelID（通知关联标识）
    public const string AppUserModelId = "TubaWinUi3.ActiveIntercept";

    private const string AppExeName = "TubaWinUi3.exe";

    /// <summary>
    /// 注册后端为 COM 服务器（写 HKCU 注册表，不需要管理员）。
    /// Windows Toast 通知点击时会通过此 COM 服务器启动后端 --toast-handler，
    /// 后端再启动主程序跳转审核页。
    /// </summary>
    public static void RegisterComServer()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return;

            using var clsidKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\CLSID\{ComGuid}");
            clsidKey.SetValue(null, "TubaWinUi3 主动拦截通知处理器");

            using var localServer = clsidKey.CreateSubKey("LocalServer32");
            localServer.SetValue(null, $"\"{exePath}\" --toast-handler");

            using var appIdKey = clsidKey.CreateSubKey("AppUserModelID");
            appIdKey.SetValue(null, AppUserModelId);

            BackEndLog.Info($"COM 服务器已注册：{exePath}");
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"COM 服务器注册失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 弹出通知（双通道：写文件 + 启动主程序 --toast 模式）。
    /// dataDir 来自后端配置（与主程序 ConfigManager.GetDataDir() 一致）。
    /// </summary>
    public static void ShowToast(string dataDir, string title, string body)
    {
        // 通道 A：写通知文件（主程序 FileSystemWatcher 监控到后弹 Toast）
        var notifFile = WriteNotificationFile(dataDir, title, body);

        // 通道 B：启动主程序 --toast 模式（无论主程序是否在运行都能弹 Toast）
        // 主程序读取通知文件 → 弹 Toast → 立即退出（不显示主窗口）。
        // 如果主程序已在运行，FileSystemWatcher 会先消费该文件，--toast 实例发现文件
        // 已被删除则不弹重复通知。
        if (!string.IsNullOrEmpty(notifFile))
        {
            TryLaunchMainAppForToast(notifFile);
        }
    }

    private static string WriteNotificationFile(string dataDir, string title, string body)
    {
        try
        {
            var notifDir = Path.Combine(dataDir, "active_intercept", "notifications");
            Directory.CreateDirectory(notifDir);

            var request = new NotificationRequest
            {
                Title = title,
                Body = body,
                TimestampUtc = DateTime.UtcNow.ToString("o"),
            };
            var json = System.Text.Json.JsonSerializer.Serialize(
                request, BackEndJsonContext.Default.NotificationRequest);
            var file = Path.Combine(notifDir,
                $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..24] + ".json");
            File.WriteAllText(file, json);
            BackEndLog.Info($"已写入通知文件：{Path.GetFileName(file)}");
            return file;
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"写通知文件失败：{ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 启动主程序 --toast 模式：主程序读取通知文件，弹出 Windows 原生 Toast 后退出（不显示主窗口）。
    /// 用户点击 Toast → COM 服务器（后端 --toast-handler）→ 启动主程序跳转审核页。
    /// </summary>
    private static void TryLaunchMainAppForToast(string notifFilePath)
    {
        try
        {
            var exe = FindMainAppExe();
            if (string.IsNullOrEmpty(exe))
            {
                BackEndLog.Warn("找不到主程序，无法弹通知");
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--toast \"{notifFilePath}\"",
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            });
            BackEndLog.Info($"已启动主程序 --toast 模式弹通知");
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"启动主程序弹通知失败：{ex.Message}");
        }
    }

    /// <summary>查找主程序可执行文件路径。</summary>
    public static string? FindMainAppExe()
    {
        var dir = AppContext.BaseDirectory;
        var exe = Path.Combine(dir, AppExeName);
        if (File.Exists(exe)) return exe;

        try
        {
            var processDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrEmpty(processDir))
            {
                exe = Path.Combine(processDir, AppExeName);
                if (File.Exists(exe)) return exe;
            }
        }
        catch { }

        return null;
    }

    /// <summary>新拦截通知。</summary>
    public static void NotifyNewBlock(string dataDir, string itemName, string subKey, string exePath)
    {
        var body = $"已自动屏蔽：{itemName}";
        if (!string.IsNullOrWhiteSpace(exePath))
            body += $"\n程序：{Truncate(exePath, 50)}";
        body += $"\n位置：{Truncate(subKey, 60)}";
        body += "\n点击查看详情并审核";
        ShowToast(dataDir, "图吧工具箱 · 主动拦截", body);
    }

    /// <summary>纠偏重新拦截通知。</summary>
    public static void NotifyReblock(string dataDir, string itemName, string subKey)
    {
        var body = $"被屏蔽项重新出现，已再次拦截：{itemName}" +
                   $"\n位置：{Truncate(subKey, 60)}" +
                   "\n点击查看详情";
        ShowToast(dataDir, "图吧工具箱 · 主动拦截纠偏", body);
    }

    private static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLen ? value : "…" + value[^maxLen..];
    }
}

/// <summary>通知请求（写入 JSON 文件，主程序读取后弹 Toast）。</summary>
public sealed class NotificationRequest
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string TimestampUtc { get; set; } = "";
}
