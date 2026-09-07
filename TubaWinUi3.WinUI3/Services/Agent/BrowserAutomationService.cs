using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using TubaWinUi3.Controls.AgentBrowser;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// AI 浏览器自动化服务：管理 <see cref="BrowserWindow"/> 单实例生命周期，
/// 通过 JS 注入实现 DOM 级操作（browser-use 类插件同思路，不依赖视觉模型）：
/// 快照可交互元素 → AI 选索引 → 点击 / 输入 / 滚动 / 提交。
/// 工具函数在后台线程执行，此处 marshal 到 UI 线程操作 WebView2。
/// </summary>
public static class BrowserAutomationService
{
    private static DispatcherQueue? _ui;
    private static BrowserWindow? _window;

    public static bool IsOpen => _window is not null;

    /// <summary>App 启动时注入主窗口 UI 线程队列。</summary>
    public static void Initialize(DispatcherQueue uiQueue) => _ui = uiQueue;

    private const int MaxSnapshotItems = 80;

    // ---------- 生命周期 ----------

    public static Task<string> OpenAsync()
    {
        if (_ui is null) return Task.FromResult("错误：浏览器服务未初始化");
        if (_window is not null)
        {
            _ui.TryEnqueue(() =>
            {
                try { _window!.Activate(); } catch { }
            });
            return Task.FromResult("浏览器已打开");
        }

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ok = _ui.TryEnqueue(() =>
        {
            try
            {
                _window = new BrowserWindow();
                _window.Closed += (_, _) => _window = null;
                _window.Activate();
                tcs.TrySetResult("浏览器已打开，可直接导航或等待 AI 操作");
            }
            catch (Exception ex)
            {
                _window = null;
                tcs.TrySetException(ex);
            }
        });
        if (!ok) return Task.FromResult("错误：UI 线程不可用");
        return tcs.Task;
    }

    public static Task<string> CloseAsync()
    {
        if (_ui is null || _window is null) return Task.FromResult("浏览器未打开");
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.TryEnqueue(() =>
        {
            try
            {
                _window!.Close();
                _window = null;
                tcs.TrySetResult("浏览器已关闭");
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }

    // ---------- 页面操作 ----------

    public static Task<string> NavigateAsync(string url)
        => RunAsync(w => w.NavigateAsync(url), err: "导航失败");

    public static Task<string> BackAsync() => RunAsync(w =>
    {
        if (w.Core?.CanGoBack == true) { w.Core.GoBack(); return Task.FromResult("已后退"); }
        return Task.FromResult("无法后退");
    });

    public static Task<string> ForwardAsync() => RunAsync(w =>
    {
        if (w.Core?.CanGoForward == true) { w.Core.GoForward(); return Task.FromResult("已前进"); }
        return Task.FromResult("无法前进");
    });

    /// <summary>读取页面状态：标题、URL、可交互元素列表（AI 据此选择操作对象）。</summary>
    public static Task<string> GetPageStateAsync() => RunAsync(async w =>
    {
        if (string.IsNullOrWhiteSpace(w.Core?.Source))
            return "浏览器尚未打开任何网页，请先调用 browser_navigate 打开网址";
        var json = await w.ExecuteJsAsync(SnapshotJs);
        if (json.StartsWith("ERROR:")) return json;
        return FormatSnapshot(json, w.LastDownloadInfo);
    });

    public static Task<string> ClickAsync(int index) => RunAsync(async w =>
    {
        var result = await w.ExecuteJsAsync(ClickJs(index));
        if (result.StartsWith("ERROR:")) return result;
        return result switch
        {
            "\"OK\"" => $"已点击元素 [{index}]",
            "\"NOT_FOUND\"" => $"错误：元素 [{index}] 不存在（页面可能已变化，请重新读取页面状态）",
            _ => $"点击结果：{result}"
        };
    });

    public static Task<string> TypeAsync(int index, string text) => RunAsync(async w =>
    {
        var escaped = JsonSerializer.Serialize(text);
        var result = await w.ExecuteJsAsync(TypeJs(index, escaped));
        if (result.StartsWith("ERROR:")) return result;
        return result switch
        {
            "\"OK\"" => $"已在元素 [{index}] 输入文本（{text.Length} 字符）",
            "\"NOT_FOUND\"" => $"错误：元素 [{index}] 不存在（页面可能已变化，请重新读取页面状态）",
            _ => $"输入结果：{result}"
        };
    });

    /// <summary>在元素上按键（enter=提交所在表单 / esc / tab）。</summary>
    public static Task<string> PressAsync(int index, string key) => RunAsync(async w =>
    {
        var result = await w.ExecuteJsAsync(PressJs(index, key.Trim().ToLowerInvariant()));
        if (result.StartsWith("ERROR:")) return result;
        return result switch
        {
            "\"OK\"" => $"已在元素 [{index}] 按下 {key}",
            "\"NOT_FOUND\"" => $"错误：元素 [{index}] 不存在（页面可能已变化，请重新读取页面状态）",
            _ => $"按键结果：{result}"
        };
    });

    public static Task<string> ScrollAsync(string direction) => RunAsync(async w =>
    {
        var result = await w.ExecuteJsAsync(ScrollJs(direction.Trim().ToLowerInvariant()));
        return result.StartsWith("ERROR:")
            ? result
            : $"已滚动页面（{direction}）";
    });

    /// <summary>读取页面正文文本（AI 阅读长内容用）。</summary>
    public static Task<string> GetPageTextAsync() => RunAsync(async w =>
    {
        if (string.IsNullOrWhiteSpace(w.Core?.Source))
            return "浏览器尚未打开任何网页，请先调用 browser_navigate 打开网址";
        var json = await w.ExecuteJsAsync(PageTextJs);
        if (json.StartsWith("ERROR:")) return json;
        try
        {
            var text = JsonSerializer.Deserialize<string>(json);
            return string.IsNullOrWhiteSpace(text) ? "（页面无可读文本）" : text;
        }
        catch { return "（无法解析页面文本）"; }
    });

    /// <summary>
    /// 执行自定义 JavaScript（高强度控制）。返回脚本结果：
    /// 数字/布尔/null 原样，对象为 JSON 文本，字符串自动解包去引号。
    /// </summary>
    public static Task<string> RunJsAsync(string script) => RunAsync(async w =>
    {
        if (string.IsNullOrWhiteSpace(w.Core?.Source))
            return "浏览器尚未打开任何网页，请先调用 browser_navigate 打开网址";
        var result = await w.ExecuteJsAsync(script);
        if (result.StartsWith("ERROR:")) return result;
        return UnwrapStringResult(result);
    });

    /// <summary>字符串结果解包（ExecuteScriptAsync 对 JS 字符串返回带引号的 JSON）。</summary>
    private static string UnwrapStringResult(string result)
    {
        if (result.Length >= 2 && result[0] == '"' && result[^1] == '"')
        {
            try { return JsonSerializer.Deserialize<string>(result) ?? result; } catch { }
        }
        return result;
    }

    // ---------- 内部 ----------

    private static async Task<string> RunAsync(Func<BrowserWindow, Task<string>> action, string err = "操作失败")
    {
        if (_ui is null) return "错误：浏览器服务未初始化";
        if (_window is null) return "错误：浏览器未打开（请先调用 browser_open）";

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ok = _ui.TryEnqueue(async () =>
        {
            try
            {
                await _window!.WhenReadyAsync();
                tcs.TrySetResult(await action(_window));
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetResult("操作已取消");
            }
            catch (Exception ex)
            {
                tcs.TrySetResult($"{err}：{ex.Message}");
            }
        });
        if (!ok) return "错误：UI 线程不可用";
        return await tcs.Task;
    }

    private static string FormatSnapshot(string json, string? downloadInfo = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // 快照脚本返回 JSON 字符串 → ExecuteScriptAsync 会再次 JSON 编码
            // （双重编码，根是 String）→ 递归解包一层拿到真正的快照对象
            if (root.ValueKind == JsonValueKind.String)
            {
                var inner = root.GetString();
                if (string.IsNullOrWhiteSpace(inner))
                    return "（页面无可读内容）";
                return FormatSnapshot(inner, downloadInfo);
            }
            var url = GetStringProp(root, "url");
            var title = GetStringProp(root, "title");
            var items = root.TryGetProperty("items", out var it) && it.ValueKind == JsonValueKind.Array ? it : default;

            var sb = new StringBuilder();
            sb.AppendLine($"页面：{title}");
            sb.AppendLine($"URL：{url}");
            if (!string.IsNullOrWhiteSpace(downloadInfo))
                sb.AppendLine($"最近下载：{downloadInfo}");
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            {
                sb.AppendLine("可交互元素：无（可能需要滚动或导航）");
                return sb.ToString();
            }

            var count = items.GetArrayLength();
            sb.AppendLine($"可交互元素（共 {count} 个，用索引操作）：");
            var shown = 0;
            foreach (var el in items.EnumerateArray())
            {
                if (shown >= MaxSnapshotItems) break;
                // 类型防御：个别页面返回的元素可能不是对象（字符串/数字等），跳过
                if (el.ValueKind != JsonValueKind.Object) continue;
                var i = GetIntProp(el, "i");
                var tag = GetStringProp(el, "tag");
                var type = GetStringProp(el, "type");
                var role = GetStringProp(el, "role");
                var text = GetStringProp(el, "text");
                var href = GetStringProp(el, "href");
                var placeholder = GetStringProp(el, "placeholder");

                var kind = DescribeKind(tag, type, role);
                var label = Truncate(text.Length > 0 ? text : placeholder.Length > 0 ? $"（{placeholder}）" : href);
                var line = $"[{i}] {kind} {label}";
                if (kind == "链接" && !string.IsNullOrWhiteSpace(href))
                    line += $" → {Truncate(href, 80)}";
                sb.AppendLine(line);
                shown++;
            }
            if (count > shown)
                sb.AppendLine($"…还有 {count - shown} 个元素（用 browser_scroll 滚动后重新读取）");
            return sb.ToString();
        }
        catch (JsonException ex)
        {
            // 页面返回的内容不是合法 JSON（旧内核/特殊页面可能返回错误文本）
            return $"页面快照解析失败：{ex.Message}（页面返回：{Truncate(json, 120)}）";
        }
        catch (Exception ex)
        {
            // 正常 JSON 却抛非解析异常 —— 记录类型与堆栈以定位（WinRT 封送层异常）
            var stack = ex.StackTrace ?? "";
            stack = stack.Length > 1500 ? stack[..1500] + "…" : stack;
            AgentDebugLog.Error("FormatSnapshot 非 JSON 异常", ex);
            return $"页面快照解析失败（非 JSON 解析错误）[{ex.GetType().Name}]：{ex.Message}\n{stack}\n（页面返回：{Truncate(json, 600)}）";
        }
    }

    /// <summary>安全读取字符串字段（元素非对象或字段类型不为字符串时返回空，防任何非常规 JSON）。</summary>
    private static string GetStringProp(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return "";
        return el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    }

    /// <summary>安全读取数字字段（元素非对象或类型不为数字时返回 -1）。</summary>
    private static int GetIntProp(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return -1;
        return el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) ? v : -1;
    }

    private static string DescribeKind(string tag, string type, string role)
    {
        var t = type.ToLowerInvariant();
        var r = role.ToLowerInvariant();
        // ARIA role 优先（自定义组件通常带 role）
        if (r.Length > 0)
        {
            var named = r switch
            {
                "button" => "按钮",
                "link" => "链接",
                "tab" => "标签页",
                "checkbox" => "复选框",
                "radio" => "单选框",
                "switch" => "开关",
                "menuitem" => "菜单项",
                "option" => "选项",
                "combobox" => "下拉框",
                "searchbox" => "搜索框",
                "slider" => "滑块",
                "spinbutton" => "数字框",
                "gridcell" => "单元格",
                "treeitem" => "树节点",
                "listbox" => "列表",
                "tabpanel" => "面板",
                "heading" => "标题",
                _ => ""
            };
            if (named.Length > 0) return named;
        }
        return tag switch
        {
            "a" => "链接",
            "button" => t == "submit" ? "提交按钮" : "按钮",
            "input" => t switch
            {
                "checkbox" => "复选框",
                "radio" => "单选框",
                "submit" => "提交按钮",
                "password" => "密码框",
                "search" => "搜索框",
                _ => "输入框"
            },
            "textarea" => "文本框",
            "select" => "下拉框",
            "summary" => "折叠项",
            "details" => "折叠区",
            "label" => "标签",
            "option" => "选项",
            "img" => "图片",
            "canvas" => "画布",
            "audio" => "音频",
            "video" => "视频",
            _ => tag
        };
    }

    private static string Truncate(string text, int max = 60)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length > max ? text[..max] + "…" : text;
    }

    // ---------- JS 脚本 ----------

    /// <summary>扫描页面可交互元素：过滤不可见项、打 data-agent-id 标记、收集文本属性。</summary>
    private const string SnapshotJs = """
        (() => {
            if (!document.body) return JSON.stringify({ url: location.href, title: document.title, items: [] });
            const seen = new Set();
            const items = [];
            let k = 0;
            // 覆盖主流交互元素 + 全部常见 ARIA role + 可聚焦/可编辑元素
            const els = document.querySelectorAll(
                'a,button,input,textarea,select,summary,details,label,option,img,canvas,audio,video,' +
                '[role="button"],[role="link"],[role="tab"],[role="checkbox"],[role="radio"],' +
                '[role="switch"],[role="menuitem"],[role="option"],[role="combobox"],[role="searchbox"],' +
                '[role="slider"],[role="spinbutton"],[role="gridcell"],[role="treeitem"],[role="listbox"],' +
                '[role="tabpanel"],[role="heading"],[contenteditable="true"],[onclick],[tabindex]');
            const interactiveTags = ['a','button','input','textarea','select','summary','details','label','option','img'];
            for (const el of els) {
                if (seen.has(el)) continue;
                seen.add(el);
                const r = el.getBoundingClientRect();
                if (r.width < 2 || r.height < 2) continue;
                const st = getComputedStyle(el);
                if (st.visibility === 'hidden' || st.display === 'none') continue;
                const tag = el.tagName.toLowerCase();
                const role = el.getAttribute('role') || '';
                const aria = el.getAttribute('aria-label') || '';
                const hasText = (el.innerText || '').trim().length > 0;
                // 低信息量过滤：仅因 tabindex/onclick 命中、无 role/aria/文本的非交互标签 → 跳过
                if (!interactiveTags.includes(tag) && !role && !aria && !hasText) continue;
                el.setAttribute('data-agent-id', String(k));
                const text = (el.innerText || el.value || aria || el.getAttribute('title') ||
                              el.placeholder || el.textContent || '')
                             .trim().replace(/\s+/g, ' ').slice(0, 80);
                // SVG 元素的 href 是 SVGAnimatedString 对象 → 强制转字符串，防止 JSON 出现对象
                const href = el.getAttribute ? String(el.getAttribute('href') || '') : '';
                const ph = typeof el.placeholder === 'string' ? el.placeholder : '';
                const tp = typeof el.type === 'string' ? el.type : '';
                items.push({ i: k, tag: tag, type: tp, role: role,
                             text: text, href: href, placeholder: ph });
                k++;
            }
            return JSON.stringify({ url: location.href, title: document.title, count: items.length, items: items });
        })();
        """;

    private static string ClickJs(int index) => $$"""
        (() => {
            const el = document.querySelector('[data-agent-id="{{index}}"]');
            if (!el) return 'NOT_FOUND';
            el.scrollIntoView({ block: 'center', behavior: 'smooth' });
            // 点击 target="_blank" 链接会开新标签页逃出 AI 控制范围：
            // 先把目标改回当前页（等价于"在新标签页打开"改为"导航"），
            // 页面内 window.open() 等其他路径由 WebView2 NewWindowRequested 拦截兜底。
            if (el.tagName === 'A' && (el.target === '_blank' || el.getAttribute('rel') === 'external')) {
                el.target = '_self';
                el.removeAttribute('rel');
            }
            el.click();
            return 'OK';
        })();
        """;

    private static string TypeJs(int index, string escapedText) => $$"""
        (() => {
            const el = document.querySelector('[data-agent-id="{{index}}"]');
            if (!el) return 'NOT_FOUND';
            el.focus();
            const proto = el instanceof HTMLTextAreaElement ? window.HTMLTextAreaElement.prototype
                        : window.HTMLInputElement.prototype;
            const setter = Object.getOwnPropertyDescriptor(proto, 'value');
            if (setter && setter.set) setter.set.call(el, {{escapedText}});
            el.dispatchEvent(new Event('input', { bubbles: true }));
            el.dispatchEvent(new Event('change', { bubbles: true }));
            return 'OK';
        })();
        """;

    private static string PressJs(int index, string key) => $$"""
        (() => {
            const el = document.querySelector('[data-agent-id="{{index}}"]');
            if (!el) return 'NOT_FOUND';
            if ('{{key}}' === 'enter') {
                const form = el.closest('form');
                if (form) { form.requestSubmit(); return 'OK'; }
                el.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, bubbles: true }));
                el.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter', code: 'Enter', keyCode: 13, bubbles: true }));
                return 'OK';
            }
            if ('{{key}}' === 'esc' || '{{key}}' === 'tab') {
                const k = '{{key}}' === 'esc' ? 'Escape' : 'Tab';
                el.dispatchEvent(new KeyboardEvent('keydown', { key: k, code: k, bubbles: true }));
                return 'OK';
            }
            return 'UNSUPPORTED_KEY';
        })();
        """;

    private static string ScrollJs(string direction) => direction switch
    {
        "bottom" => "window.scrollTo({ top: document.body.scrollHeight, behavior: 'smooth' });",
        "top" => "window.scrollTo({ top: 0, behavior: 'smooth' });",
        "up" => "window.scrollBy({ top: -600, behavior: 'smooth' });",
        "down" => "window.scrollBy({ top: 600, behavior: 'smooth' });",
        _ => "window.scrollBy({ top: 600, behavior: 'smooth' });"
    };

    private const string PageTextJs = """
        (() => {
            const main = document.querySelector('main, article, #content, .content, [role="main"]') || document.body;
            const text = (main.innerText || '')
                .replace(/[ \t]+\n/g, '\n')
                .replace(/\n{3,}/g, '\n\n')
                .trim()
                .slice(0, 6000);
            return text;
        })();
        """;

}
