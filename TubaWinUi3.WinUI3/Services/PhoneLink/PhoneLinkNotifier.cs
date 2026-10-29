using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;

namespace TubaWinUi3.Services;

/// <summary>
/// 「连接手机」原生通知：手机消息、任务开始/结束弹 Windows Toast，点击回跳聊天或任务弹窗。
/// 通知身份固定在 <see cref="AppUserModelId"/>（HKCU 注册 DisplayName「空岛图吧工具箱」+ 横幅开关），
/// 点击经 CustomActivator（LocalServer32 → 本程序 --phone-toast-handler）转发给主实例。
/// 另订阅 Toolkit 的 OnActivated 作为防御通道（历史通知/注册被工具包自建身份接管的场景）。
/// 注意：MSIX 打包版注册表会被虚拟化，壳层解析不到该注册，点击暂不生效（通知仍会正常显示）。
/// </summary>
public static class PhoneLinkNotifier
{
    // 连接手机通知专用的 COM 服务器 GUID
    private const string ComGuid = "{A1E5D7C3-6B92-4F48-9C31-2D8E4A6B1F70}";
    public const string AppUserModelId = "TubaWinUi3.PhoneLink";
    public const string HandlerArg = "--phone-toast-handler";
    private const string ToastGroup = "phonelink";
    private const string AppDisplayName = "空岛图吧工具箱";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private static bool _registered;
    private static bool _initialized;

    /// <summary>
    /// 进程级通知身份 + 注册（幂等）。必须早于任何 Toast（App 构造函数调用）：
    /// 未固定的进程会走工具包自建的按 exe 路径身份——名字不对、横幅还可能被系统关过，点击也回不来。
    /// </summary>
    public static void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;
        try
        {
            if (!RuntimeHelper.IsMsixPackaged) SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PhoneLink] 设置进程通知身份失败：{ex.Message}");
        }
        RegisterComServer();
    }

    /// <summary>主实例启动时调用一次：确保注册 + 订阅消息/任务事件。</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        EnsureRegistered();
        PhoneLinkService.MessageAdded += OnMessageAdded;
        PhoneLinkService.JobStarted += OnJobStarted;
        PhoneLinkService.JobFinished += OnJobFinished;
    }

    // ───────────────────────── 点击回跳注册 ─────────────────────────

    /// <summary>注册为 Toast 点击的 COM 服务器（HKCU，无需管理员），仿 NotificationHelper.RegisterComServer。</summary>
    public static void RegisterComServer()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return;

            using (var clsid = Registry.CurrentUser.CreateSubKey($@"Software\Classes\CLSID\{ComGuid}"))
            {
                clsid.SetValue(null, "TubaWinUi3 通知点击处理器");
                using (var localServer = clsid.CreateSubKey("LocalServer32"))
                    localServer.SetValue(null, $"\"{exePath}\" {HandlerArg}");
                using (var appId = clsid.CreateSubKey("AppUserModelID"))
                    appId.SetValue(null, AppUserModelId);
            }

            using (var aumid = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}"))
            {
                aumid.SetValue("DisplayName", AppDisplayName);
                aumid.SetValue("CustomActivator", ComGuid);
                var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
                if (File.Exists(icon)) aumid.SetValue("IconUri", new Uri(icon).AbsoluteUri);
            }

            // 首次注册时补上通知默认值（横幅 + 通知中心）。已存在则不覆盖——尊重用户后来手动改的设置。
            var settingsPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings\{AppUserModelId}";
            using var existing = Registry.CurrentUser.OpenSubKey(settingsPath);
            if (existing is null)
            {
                using var settings = Registry.CurrentUser.CreateSubKey(settingsPath);
                settings.SetValue("ShowBanner", 1, RegistryValueKind.DWord);
                settings.SetValue("ShowInActionCenter", 1, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PhoneLink] 通知注册失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 解析 --phone-toast-handler 之后的激活参数（Windows 把 Toast 参数按 key=value 追加到命令行）。
    /// 返回 (Action, Target, JobId)：Action = phone-link / show-active-intercept，Target = chat / jobs。
    /// </summary>
    internal static (string Action, string Target, string? JobId) ParseHandlerArgs(IEnumerable<string> args)
    {
        var action = "";
        var target = "";
        var job = "";
        foreach (var raw in args)
        {
            var token = raw.Trim().Trim('"');
            if (token.Length == 0) continue;
            var eq = token.IndexOf('=');
            if (eq > 0)
            {
                var key = token[..eq].Trim();
                var value = token[(eq + 1)..].Trim();
                if (key.Equals("action", StringComparison.OrdinalIgnoreCase)) action = value;
                else if (key.Equals("target", StringComparison.OrdinalIgnoreCase)) target = value;
                else if (key.Equals("job", StringComparison.OrdinalIgnoreCase)) job = value;
            }
            else if (token.Contains("show-active-intercept", StringComparison.OrdinalIgnoreCase))
            {
                // 极端情况下参数可能被拆散，保底识别主动拦截的动作
                action = "show-active-intercept";
            }
        }
        return (action, target, job.Length == 0 ? null : job);
    }

    /// <summary>解析 Toolkit 传给 OnActivated 的参数串（"action=phone-link&amp;target=chat&amp;job=…"）。</summary>
    internal static (string Action, string Target, string? JobId) ParseArgumentString(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument)) return ("", "", null);
        var parsed = argument.Replace("&amp;", "&");
        return ParseHandlerArgs(parsed.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Uri.UnescapeDataString(part)));
    }

    // ───────────────────────── 弹通知 ─────────────────────────

    private static void OnMessageAdded(PhoneChatMessage m)
    {
        if (m.From != "phone") return;
        if (PhoneLinkUiState.ChatDialogVisible) return;
        var body = m.Type switch
        {
            "text" => Truncate(m.Text.ReplaceLineEndings(" "), 140),
            "image" => $"[图片] {m.FileName}",
            _ => $"[文件] {m.FileName}"
        };
        if (string.IsNullOrWhiteSpace(body)) body = m.Type == "image" ? "[图片]" : "[文件]";
        // 消息用强提醒：横幅不在几秒后消失，停留到用户处理（专注助手也会放行）
        ShowToast("收到手机消息", body, "chat", null, $"msg-{m.Seq}", strong: true);
    }

    private static void OnJobStarted(PhoneJob job)
    {
        if (PhoneLinkUiState.JobsDialogVisible) return;
        ShowToast(
            job.Kind == PhoneJobKind.Exec ? "手机请求执行命令" : "手机请求安装应用",
            Truncate(job.Title.ReplaceLineEndings(" "), 140),
            "jobs", job.Id, JobTag(job.Id));
    }

    private static void OnJobFinished(PhoneJob job)
    {
        var title = job.Kind == PhoneJobKind.Exec
            ? job.Status switch
            {
                PhoneJobStatus.Cancelled => "命令已终止",
                PhoneJobStatus.Done => "命令执行完成",
                _ => $"命令执行失败（退出码 {job.ExitCode}）"
            }
            : job.Status switch
            {
                PhoneJobStatus.Cancelled => "应用安装已终止",
                PhoneJobStatus.Done => "应用安装完成",
                _ => $"应用安装失败（退出码 {job.ExitCode}）"
            };
        ShowToast(title, Truncate(job.Title.ReplaceLineEndings(" "), 140), "jobs", job.Id, JobTag(job.Id));
    }

    private static string JobTag(string jobId) => $"job-{jobId}";

    private static void ShowToast(string title, string body, string target, string? jobId, string tag, bool strong = false)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .AddArgument("action", "phone-link")
                .AddArgument("target", target);
            if (!string.IsNullOrEmpty(jobId)) builder.AddArgument("job", jobId);
            if (strong)
            {
                builder.SetToastScenario(ToastScenario.Reminder);
                builder.AddButton(new ToastButton()
                    .SetContent("查看会话")
                    .AddArgument("action", "phone-link")
                    .AddArgument("target", target));
            }
            builder.Show(toast =>
            {
                toast.Tag = tag;
                toast.Group = ToastGroup;
                toast.ExpirationTime = DateTimeOffset.Now.AddMinutes(30);
            });
        }
        catch (Exception ex)
        {
            // 打包模式/无 AUMID 注册时会抛，吞掉不影响主流程（与 DownloadQueueService 一致）
            Debug.WriteLine($"[PhoneLink] 弹通知失败：{ex.Message}");
        }
    }

    internal static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..max] + "…";
    }
}
