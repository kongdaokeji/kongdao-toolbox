using System.Text;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Controls.AgentBrowser;

/// <summary>
/// AI 可操作的浏览器窗口：独立 WebView2 实例，通过 JavaScript 注入
/// 实现 DOM 级自动化（元素快照 / 点击 / 输入 / 滚动），不需要视觉模型。
/// 所有方法供 <see cref="BrowserAutomationService"/> 在 UI 线程调用。
/// </summary>
public sealed partial class BrowserWindow : Window
{
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BrowserWindow()
    {
        InitializeComponent();
        Title = "图吧助手 · AI 浏览器";
        try
        {
            var presenter = AppWindow.Presenter as OverlappedPresenter;
            if (presenter is not null) presenter.IsResizable = true;
            AppWindow.Resize(new SizeInt32(1120, 800));
        }
        catch { }
        Closed += (_, _) => _ = ShutdownCoreAsync();
        _ = InitCoreAsync();
    }

    /// <summary>WebView2 核心就绪（首次调用前等待）。</summary>
    public Task WhenReadyAsync() => _readyTcs.Task;

    public CoreWebView2? Core => Web.CoreWebView2;

    /// <summary>最近一次下载信息（文件名 → 路径 + 状态），供 AI 查询。</summary>
    public string? LastDownloadInfo { get; private set; }

    /// <summary>
    /// 待返回的脚本请求（id → 完成源）。
    /// 通过 WebMessage 通道执行脚本（PostWebMessageAsJson），
    /// 绕开 CoreWebView2.ExecuteScriptAsync 在部分 CsWinRT projection
    /// 上的类型封送缺陷（"requires an element of type 'Object'" 类错误）。
    /// </summary>
    private readonly Dictionary<string, TaskCompletionSource<string>> _pendingScripts = [];
    private long _scriptSeq;

    private async Task InitCoreAsync()
    {
        try
        {
            // 使用共享 WebView2 环境：用户数据目录固定在 %LocalAppData%\TubaWinUi3\WebView2，
            // 与安装位置解耦（默认目录在 exe 旁，安装目录不可写时会报"无法读取和写入其数据目录"）
            await Web.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync());

            Core!.NavigationCompleted += (_, e) =>
            {
                AddressBox.Text = Web.Source?.ToString() ?? "";
                StatusText.Text = e.IsSuccess ? "就绪" : $"导航失败：{DescribeNavigationError(e.WebErrorStatus)}";
            };
            Core.DocumentTitleChanged += (_, _) =>
            {
                var t = Core.DocumentTitle;
                if (!string.IsNullOrWhiteSpace(t)) Title = $"图吧助手 · AI 浏览器 — {t}";
            };
            Core.WebMessageReceived += OnWebMessageReceived;

            // 拦截新窗口/新标签页：页面 target="_blank"、window.open() 等会触发本事件。
            // 原样放行会让链接逃出本 WebView（跳出 AI 控制范围），一律改为当前页强制导航。
            Core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (string.IsNullOrWhiteSpace(e.Uri))
                {
                    StatusText.Text = "已拦截新窗口（无地址，已忽略）";
                    return;
                }
                try
                {
                    Core.Navigate(e.Uri);
                    StatusText.Text = $"已拦截新窗口，强制导航至 {Truncate(e.Uri, 60)}";
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"拦截新窗口失败：{CleanWinRtMessage(ex)}";
                }
            };
            // 常驻消息监听器：优先在文档创建前注入；失败时降级为导航完成后注入（幂等）
            try
            {
                await Core.AddScriptToExecuteOnDocumentCreatedAsync(ListenerJs);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"listener 预注入失败：{ex.Message}");
            }
            Core.NavigationCompleted += async (_, _) =>
            {
                try { await Web.ExecuteScriptAsync(ListenerJs); } catch { }
            };

            // 接管下载：自动保存到用户下载文件夹，并记录供 AI 汇报（不弹系统下载对话框）
            Core.DownloadStarting += (_, e) =>
            {
                try
                {
                    var suggested = e.ResultFilePath; // 系统建议路径（含文件名）
                    if (string.IsNullOrWhiteSpace(suggested)) return;
                    var fileName = Path.GetFileName(suggested);
                    if (string.IsNullOrWhiteSpace(fileName)) return;
                    var downloads = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    Directory.CreateDirectory(downloads);
                    var fullPath = Path.Combine(downloads, fileName);
                    e.ResultFilePath = fullPath;
                    e.Handled = true;
                    LastDownloadInfo = $"{fileName} → {fullPath}（下载中）";
                    StatusText.Text = $"下载中：{fileName}";
                    e.DownloadOperation.StateChanged += (_, _) =>
                    {
                        if (e.DownloadOperation.State == CoreWebView2DownloadState.Completed)
                        {
                            LastDownloadInfo = $"{fileName} → {fullPath}（已完成）";
                            StatusText.Text = $"已下载：{fileName}";
                        }
                        else if (e.DownloadOperation.State == CoreWebView2DownloadState.Interrupted)
                        {
                            LastDownloadInfo = $"{fileName} 下载中断：{e.DownloadOperation.InterruptReason}";
                            StatusText.Text = "下载中断";
                        }
                    };
                }
                catch { }
            };

            _readyTcs.TrySetResult();
        }
        catch (Exception ex)
        {
            _readyTcs.TrySetException(ex);
        }
    }

    private async Task ShutdownCoreAsync()
    {
        try
        {
            await Web.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync());
            if (Core is not null)
                Core.Profile.ClearBrowsingDataAsync();
            Web.Close();
        }
        catch { }
    }

    // ---------- 导航 ----------

    public Task<string> NavigateAsync(string url)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Core is null)
        {
            tcs.TrySetResult("错误：浏览器内核未就绪");
            return tcs.Task;
        }

        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            Core!.NavigationCompleted -= Handler;
            tcs.TrySetResult(e.IsSuccess
                ? "OK"
                : $"导航失败：{DescribeNavigationError(e.WebErrorStatus)}。可稍后重试，或更换镜像/代理地址");
        }

        Core.NavigationCompleted += Handler;
        try
        {
            Core.Navigate(url);
        }
        catch (Exception ex)
        {
            Core.NavigationCompleted -= Handler;
            tcs.TrySetException(ex);
        }

        // 防挂起：15 秒无结果视为超时。
        // 注意：ContinueWith 在线程池线程执行，必须经 DispatcherQueue 回到 UI 线程
        // 才能触碰 CoreWebView2（COM 对象有 UI 线程亲和性，否则抛 RPC_E_WRONG_THREAD）；
        // 回调整体 try/catch，避免产生未观察异常（finalizer 线程会抛出 AggregateException）。
        _ = Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(_ =>
        {
            try
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (tcs.Task.IsCompleted) return;
                    try { Core.NavigationCompleted -= Handler; } catch { }
                    tcs.TrySetResult("导航超时（15 秒），请重试");
                });
            }
            catch { }
        });
        return tcs.Task;
    }

    // ---------- JavaScript 执行 ----------

    /// <summary>
    /// 执行 JS 并返回结果（JSON 字符串）。
    /// 主通道：CoreWebView2.ExecuteScriptAsync（自测验证可用）；
    /// 若抛类型封送类异常（部分页面/时序下 "Object/String" 错误），
    /// 自动降级到 WebMessage 通道重试一次。
    /// </summary>
    public async Task<string> ExecuteJsAsync(string script)
    {
        if (Core is null) return "ERROR:浏览器内核未就绪";
        try
        {
            var result = await Core.ExecuteScriptAsync(script);
            if (result.Length == 0) return "ERROR:脚本执行无结果（页面可能未就绪，请稍后重试）";
            // 内核正常时返回 JSON 字符串；个别 Runtime/页面组合会返回错误文本 —— 拦截并明确报错
            if (!LooksLikeJson(result))
                return $"ERROR:页面脚本返回异常内容：{Truncate(result, 200)}";
            return result;
        }
        catch (Exception ex)
        {
            AgentDebugLog.Error("ExecuteScriptAsync 失败，降级 WebMessage 通道", ex);
            return await ExecuteJsViaWebMessageAsync(script);
        }
    }

    /// <summary>ExecuteScriptAsync 的正常返回值是 JSON（对象/数组/字符串/数字/布尔/null）。</summary>
    private static bool LooksLikeJson(string s)
    {
        var c = s.Length > 0 ? s[0] : '\0';
        return c is '{' or '[' or '"' or 't' or 'f' or 'n' or '-' || char.IsDigit(c);
    }

    private static string Truncate(string s, int max)
        => s.Length > max ? s[..max] + "…" : s;

    /// <summary>备选通道：PostWebMessageAsJson → 页面常驻监听器执行 → postMessage 回传。</summary>
    private async Task<string> ExecuteJsViaWebMessageAsync(string script)
    {
        var id = Interlocked.Increment(ref _scriptSeq).ToString();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingScripts[id] = tcs;
        try
        {
            var payload = $"{{\"id\":{JsonSerializer.Serialize(id)},\"script\":{JsonSerializer.Serialize(script)}}}";
            Core!.PostWebMessageAsJson(payload);
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(12));
        }
        catch (TimeoutException)
        {
            return "ERROR:脚本执行超时（12 秒，页面可能正在加载，请稍后重试）";
        }
        catch (Exception ex)
        {
            return "ERROR:" + CleanWinRtMessage(ex);
        }
        finally
        {
            _pendingScripts.Remove(id);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var raw = e.WebMessageAsJson;
            // listener 里 postMessage 的是 JSON.stringify 后的对象，WebView2 会再次编码 →
            // 收到的可能是双重编码的 JSON 字符串，先解包一层
            if (!string.IsNullOrWhiteSpace(raw))
            {
                using var outer = JsonDocument.Parse(raw);
                if (outer.RootElement.ValueKind == JsonValueKind.String)
                    raw = outer.RootElement.GetString() ?? "";
            }
            if (string.IsNullOrWhiteSpace(raw)) return;

            using var doc = JsonDocument.Parse(raw);
            var id = doc.RootElement.GetProperty("id").GetString();
            if (id is null || !_pendingScripts.TryGetValue(id, out var tcs)) return;

            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.GetString() is { Length: > 0 } errMsg)
            {
                tcs.TrySetResult("ERROR:" + errMsg);
            }
            else if (doc.RootElement.TryGetProperty("result", out var res))
            {
                tcs.TrySetResult(res.GetString() ?? "null");
            }
        }
        catch { }
    }

    /// <summary>页面常驻监听器：接收脚本指令、执行、回传结果（在文档创建时注入）。</summary>
    private const string ListenerJs = """
        (() => {
            if (window.__tubaAgentListener) return;
            window.__tubaAgentListener = true;
            window.chrome.webview.addEventListener('message', (ev) => {
                const msg = ev.data || {};
                if (!msg || typeof msg.script !== 'string') return;
                let result = null, error = '';
                try {
                    result = (0, eval)('(' + msg.script + '\n)');
                } catch (ex) {
                    error = String((ex && ex.message) || ex);
                }
                try {
                    window.chrome.webview.postMessage(
                        JSON.stringify({ id: msg.id, ok: error.length === 0, result: result, error: error }));
                } catch (ex) {
                    window.chrome.webview.postMessage(
                        JSON.stringify({ id: msg.id, ok: true, result: String(result), error: '' }));
                }
            });
        })();
        """;

    /// <summary>剥离 WinRT 异常的 "A4F#M: " 前缀，保留可读信息。</summary>
    internal static string CleanWinRtMessage(Exception ex)
    {
        var msg = ex.Message ?? "";
        var idx = msg.IndexOf(':');
        if (idx is > 0 and <= 8)
        {
            var head = msg[..idx].Trim();
            if (head.Length is >= 3 and <= 6 &&
                head.All(c => char.IsLetterOrDigit(c) || c is '#' or '_'))
            {
                msg = msg[(idx + 1)..].Trim();
            }
        }
        return msg.Length > 160 ? msg[..160] + "…" : msg;
    }

    /// <summary>导航错误码 → 中文可操作描述。</summary>
    private static string DescribeNavigationError(CoreWebView2WebErrorStatus status) => status switch
    {
        CoreWebView2WebErrorStatus.Timeout => "加载超时",
        CoreWebView2WebErrorStatus.ConnectionAborted => "连接被中断（网络不稳定或被防火墙/运营商拦截）",
        CoreWebView2WebErrorStatus.ConnectionReset => "连接被重置（可能被网络环境拦截）",
        CoreWebView2WebErrorStatus.HostNameNotResolved => "域名解析失败（该地址无法访问）",
        CoreWebView2WebErrorStatus.ServerUnreachable => "服务器不可达",
        CoreWebView2WebErrorStatus.CannotConnect => "无法建立连接",
        CoreWebView2WebErrorStatus.Disconnected => "网络已断开",
        CoreWebView2WebErrorStatus.CertificateIsInvalid => "证书无效",
        CoreWebView2WebErrorStatus.CertificateExpired => "证书已过期",
        CoreWebView2WebErrorStatus.CertificateRevoked => "证书已被吊销",
        CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect => "证书域名不匹配",
        _ => status.ToString()
    };

    // ---------- 工具栏事件 ----------

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Core?.CanGoBack == true) Core.GoBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Core?.CanGoForward == true) Core.GoForward();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        Core?.Reload();
    }

    private void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        var text = AddressBox.Text.Trim();
        if (text.Length == 0) return;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            text = "https://www.bing.com/search?q=" + Uri.EscapeDataString(text);
        else if (uri.Scheme is not ("http" or "https"))
            text = "https://www.bing.com/search?q=" + Uri.EscapeDataString(text);
        // fire-and-forget：观察异常，避免未观察异常在 finalizer 线程被重新抛出
        _ = NavigateAsync(text).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }
}
