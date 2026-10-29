using System.Diagnostics;
using System.Text.Json;

namespace TubaWinUi3.Services;

/// <summary>Toast 点击的激活请求（跨进程通过文件传递：处理器进程写、主实例读）。</summary>
public sealed class PhoneLinkActivationRequest
{
    /// <summary>"chat" | "jobs"（空 = 只打开连接手机页面不开弹窗）。</summary>
    public string Target { get; set; } = "";
    public string? JobId { get; set; }
    public string TimeUtc { get; set; } = "";
}

/// <summary>
/// 连接手机的点击激活通道：
/// <c>--phone-toast-handler</c> 进程写激活文件（主实例在跑）或直接记 Pending（主实例不在）；
/// 主实例用 FileSystemWatcher 监听激活目录，消费后恢复窗口、打开聊天/任务弹窗。
/// </summary>
public static class PhoneLinkActivation
{
    private static readonly object _pendingLock = new();
    private static PhoneLinkActivationRequest? _pending;
    private static FileSystemWatcher? _watcher;

    public static string ActivationDir => Path.Combine(ConfigManager.GetDataDir(), "phone_link", "activation");

    /// <summary>本进程启动时由 --phone-toast-handler 直接塞入的待处理目标（无主实例场景）。</summary>
    public static void SetPending(string target, string? jobId)
    {
        lock (_pendingLock) _pending = new PhoneLinkActivationRequest { Target = target, JobId = jobId, TimeUtc = DateTime.UtcNow.ToString("o") };
    }

    /// <summary>是否存在待处理目标（主实例启动后据此决定是否导航到连接手机页）。</summary>
    public static bool HasPending
    {
        get { lock (_pendingLock) return _pending is not null; }
    }

    /// <summary>取出并清空待处理目标（页面 Loaded 时调用；没有则返回 null）。</summary>
    public static PhoneLinkActivationRequest? TryConsume()
    {
        lock (_pendingLock)
        {
            var p = _pending;
            _pending = null;
            return p;
        }
    }

    /// <summary>写激活请求文件（--phone-toast-handler 在主实例已运行时用）。</summary>
    public static bool WriteRequestFile(string target, string? jobId)
    {
        try
        {
            Directory.CreateDirectory(ActivationDir);
            var payload = JsonSerializer.Serialize(new PhoneLinkActivationRequest
            {
                Target = target,
                JobId = jobId,
                TimeUtc = DateTime.UtcNow.ToString("o")
            });
            var name = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
            File.WriteAllText(Path.Combine(ActivationDir, name[..30] + ".json"), payload);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PhoneLink] 写激活文件失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 主实例启动时调用：先处理积压的激活文件，再监听新文件。
    /// 回调在后台线程触发，调用方需自行调度到 UI 线程。
    /// </summary>
    public static void StartWatcher(Action<PhoneLinkActivationRequest> onActivated)
    {
        try
        {
            Directory.CreateDirectory(ActivationDir);
            foreach (var file in Directory.GetFiles(ActivationDir, "*.json"))
                HandleFile(file, onActivated);

            _watcher = new FileSystemWatcher(ActivationDir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            _watcher.Created += (_, e) => System.Threading.Tasks.Task.Delay(150).ContinueWith(_ => HandleFile(e.FullPath, onActivated));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PhoneLink] 激活监听启动失败：{ex.Message}");
        }
    }

    private static void HandleFile(string filePath, Action<PhoneLinkActivationRequest> onActivated)
    {
        try
        {
            if (!File.Exists(filePath)) return;
            var req = JsonSerializer.Deserialize<PhoneLinkActivationRequest>(File.ReadAllText(filePath));
            try { File.Delete(filePath); } catch { }
            if (req is not null) onActivated(req);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PhoneLink] 处理激活文件失败：{ex.Message}");
        }
    }
}
