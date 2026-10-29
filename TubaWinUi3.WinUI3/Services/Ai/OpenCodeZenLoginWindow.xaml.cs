using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;

namespace TubaWinUi3.Services.Ai;

/// <summary>
/// OpenCode Zen 登录窗口（独立 Window + WebView2，避免 ContentDialog 内 WebView2 不渲染的问题）：
/// 1. 打开控制台（opencode.ai/auth），用户用 GitHub/Google 登录；
/// 2. 登录后自动进入 API Keys 页面并创建「图吧工具箱」Key；
/// 3. 通过页面内剪贴板捕获拿到完整 Key（sk-…）—— 自动创建失败时，
///    用户手动点复制按钮也能被捕获，作为兜底。
/// 会话 Cookie 持久化在共享 WebView2 用户目录（%LocalAppData%\TubaWinUi3\WebView2），
/// 之后无需重复登录。带 Key 调用 Zen API 的免费额度远高于匿名。
/// </summary>
public sealed partial class OpenCodeZenLoginWindow : Window
{
    private const string ConsoleHome = "https://opencode.ai/auth";
    private const string KeyName = "图吧工具箱";

    private readonly TaskCompletionSource<string?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _automationBusy;

    public OpenCodeZenLoginWindow()
    {
        InitializeComponent();
        Title = "登录 OpenCode Zen 获取 API Key";
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
                presenter.IsResizable = true;
            AppWindow.Resize(new SizeInt32(880, 700));
        }
        catch { }
        Closed += (_, _) => _result.TrySetResult(null);
        _ = InitCoreAsync();
    }

    /// <summary>打开登录窗口并等待结果：成功返回 API Key（sk-…），取消/失败返回 null。</summary>
    public static async Task<string?> ShowAsync()
    {
        var window = new OpenCodeZenLoginWindow();
        window.Activate();
        return await window._result.Task;
    }

    private async Task InitCoreAsync()
    {
        try
        {
            await Web.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync());
            var core = Web.CoreWebView2;

            // 页面创建时注入剪贴板捕获：点「复制」按钮时记录完整 Key（手动操作兜底）
            await core.AddScriptToExecuteOnDocumentCreatedAsync("""
                (function () {
                  try {
                    var orig = navigator.clipboard.writeText.bind(navigator.clipboard);
                    window.__zenCapturedKey = null;
                    navigator.clipboard.writeText = function (t) { window.__zenCapturedKey = t; return orig(t); };
                  } catch (e) {}
                })();
                """);

            core.NavigationCompleted += async (_, args) =>
            {
                if (_automationBusy) return;
                _automationBusy = true;
                try
                {
                    var url = core.Source;
                    if (!args.IsSuccess)
                    {
                        StatusText.Text = $"页面加载失败：{DescribeError(args.WebErrorStatus)}（如无法访问，请开启代理/加速器后重试）";
                        return;
                    }

                    if (url.Contains("/keys"))
                    {
                        await TryAutoCreateKeyAsync(core);
                        return;
                    }

                    if (url.Contains("/workspace/"))
                    {
                        StatusText.Text = "已登录，正在打开 API Keys 页面…";
                        var m = Regex.Match(url, "/workspace/([^/?#]+)");
                        var baseUrl = m.Success ? $"https://opencode.ai/workspace/{m.Groups[1].Value}" : "https://opencode.ai/workspace-picker";
                        core.Navigate(baseUrl + "/keys");
                        return;
                    }

                    // 落地到工作区选择页：点第一个工作区
                    if (!url.Contains("/auth") && !url.Contains("auth.opencode.ai"))
                    {
                        var clickResult = await EvalAsync(core, """
                            (function () {
                              var a = document.querySelector('a[href^="/workspace/"]');
                              if (a) { a.click(); return 'clicked'; }
                              return 'none';
                            })();
                            """);
                        if (clickResult == "\"clicked\"")
                        {
                            StatusText.Text = "正在进入工作区…";
                            return;
                        }
                    }

                    StatusText.Text = "请在页面中登录（GitHub / Google）。登录后会自动创建 API Key。";
                }
                finally
                {
                    _automationBusy = false;
                }
            };

            core.Navigate(ConsoleHome);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"初始化失败：{ex.Message}";
        }
    }

    /// <summary>自动创建 Key：打开创建表单 → 填名字 → 提交 → 轮询新行并点击复制，捕获完整 Key。</summary>
    private async Task TryAutoCreateKeyAsync(CoreWebView2 core)
    {
        StatusText.Text = "正在自动创建 API Key…";

        // 1. 打开创建表单
        await EvalAsync(core, """
            (function () {
              try {
                var btn = document.querySelector('[data-slot="title-row"] button');
                if (btn) { btn.click(); return 'opened'; }
                return 'no-create-btn';
              } catch (e) { return 'err:' + e.message; }
            })();
            """);
        await Task.Delay(500);

        // 2. 填写名称并提交
        await EvalAsync(core, $$"""
            (function () {
              try {
                var input = document.querySelector('form[data-slot="create-form"] input[name="name"]');
                if (!input) return 'no-input';
                input.value = '{{KeyName}}';
                input.dispatchEvent(new Event('input', { bubbles: true }));
                var submit = document.querySelector('form[data-slot="create-form"] button[type="submit"]');
                if (!submit) return 'no-submit';
                submit.click();
                return 'submitted';
              } catch (e) { return 'err:' + e.message; }
            })();
            """);

        // 3. 轮询新行并点击复制（最多 ~15s）；用户手动点复制也会被捕获
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(750);

            var clickResult = await EvalAsync(core, """
                (function () {
                  try {
                    window.__zenCapturedKey = null;
                    var rows = document.querySelectorAll('[data-slot="key-name"]');
                    for (var i = 0; i < rows.length; i++) {
                      if (rows[i].textContent.trim() === '图吧工具箱') {
                        var row = rows[i].closest('tr');
                        var copy = row ? row.querySelector('[data-slot="key-value"] button') : null;
                        if (copy) { copy.click(); return 'clicked'; }
                      }
                    }
                    return 'not-found';
                  } catch (e) { return 'err:' + e.message; }
                })();
                """);

            if (clickResult == "\"clicked\"")
                await Task.Delay(400); // 剪贴板写入是异步的，等它落定

            var captured = await EvalAsync(core, "window.__zenCapturedKey");
            var key = DecodeJsString(captured);
            if (!string.IsNullOrWhiteSpace(key) && key.StartsWith("sk-", StringComparison.Ordinal))
            {
                StatusText.Text = $"已获取 API Key：{MaskKey(key)}，即将关闭…";
                _result.TrySetResult(key);
                await Task.Delay(900);
                try { Close(); } catch { }
                return;
            }
        }

        StatusText.Text = "自动创建未成功：请点击页面上方「Create API Key」按钮手动创建一个，然后点复制按钮（应用会自动捕获）。";
    }

    private static async Task<string> EvalAsync(CoreWebView2 core, string js)
    {
        try
        {
            return await core.ExecuteScriptAsync(js);
        }
        catch
        {
            return "err";
        }
    }

    /// <summary>ExecuteScriptAsync 返回 JSON 编码字符串，这里解出原始值。</summary>
    private static string? DecodeJsString(string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || json == "null") return null;
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.String
                ? doc.RootElement.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string MaskKey(string key)
        => key.Length <= 12 ? key : $"{key[..7]}…{key[^4..]}";

    private static string DescribeError(CoreWebView2WebErrorStatus status) => status switch
    {
        CoreWebView2WebErrorStatus.HostNameNotResolved => "无法解析域名",
        CoreWebView2WebErrorStatus.Disconnected => "网络未连接",
        CoreWebView2WebErrorStatus.Timeout => "连接超时",
        CoreWebView2WebErrorStatus.ConnectionAborted => "连接中断",
        CoreWebView2WebErrorStatus.ConnectionReset => "连接被重置",
        CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect => "证书错误",
        _ => status.ToString(),
    };
}
