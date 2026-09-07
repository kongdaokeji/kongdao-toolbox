using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 浏览器自动化工具：让 AI 操作真实浏览器（WebView2）。
/// 浏览器插件（browser-use 等）同款思路：DOM 快照 → 索引定位 → 点击/输入/滚动，
/// 完全不需要视觉模型。页面快照仅返回可交互元素的结构化描述。
/// </summary>
public static class BrowserAgentTool
{
    public static void Register()
    {
        Add("browser_open", "打开浏览器", "\uE774", (Func<CancellationToken, Task<string>>)OpenAsync);
        Add("browser_navigate", "打开网页", "\uE774", (Func<string, CancellationToken, Task<string>>)NavigateAsync);
        Add("browser_get_page", "读取页面", "\uE774", (Func<CancellationToken, Task<string>>)GetPageAsync);
        Add("browser_run_js", "执行脚本", "\uE943", (Func<string, CancellationToken, Task<string>>)RunJsAsync);
        Add("browser_click", "点击元素", "\uE71C", (Func<int, CancellationToken, Task<string>>)ClickAsync);
        Add("browser_type", "输入文本", "\uE70F", (Func<int, string, CancellationToken, Task<string>>)TypeAsync);
        Add("browser_press", "按下按键", "\uE756", (Func<int, string, CancellationToken, Task<string>>)PressAsync);
        Add("browser_scroll", "滚动页面", "\uE7C3", (Func<string, CancellationToken, Task<string>>)ScrollAsync);
        Add("browser_get_text", "读取正文", "\uE8D3", (Func<CancellationToken, Task<string>>)GetTextAsync);
        Add("browser_back", "后退", "\uE72B", (Func<CancellationToken, Task<string>>)BackAsync);
        Add("browser_close", "关闭浏览器", "\uE711", (Func<CancellationToken, Task<string>>)CloseAsync);
        Add("browser_wait_for_login", "等待登录", "\uE72E", (Func<string, string, CancellationToken, Task<string>>)WaitForLoginAsync,
            alwaysConfirm: true, defaultReason: "需要你在浏览器窗口中完成登录后继续");
    }

    [Description("打开真实的浏览器窗口（WebView2）。需要操作网页/表单/下载/登录时先调用它，之后用 browser_get_page 读取页面元素")]
    public static async Task<string> OpenAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await BrowserAutomationService.OpenAsync();
    }

    [Description("在浏览器中打开指定网址（http/https）")]
    public static async Task<string> NavigateAsync(string url, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(url)) return "错误：缺少 url 参数";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "错误：URL 无效（仅支持 http/https）";

        var result = await BrowserAutomationService.NavigateAsync(uri.ToString());
        return result == "OK"
            ? $"已打开：{uri}"
            : result;
    }

    [Description("读取浏览器当前页面：标题、URL、可交互元素列表（链接/按钮/输入框/复选框/开关等，带索引）。之后用索引调用 browser_click / browser_type / browser_press。页面未打开时自动打开浏览器")]
    public static async Task<string> GetPageAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!BrowserAutomationService.IsOpen)
            await BrowserAutomationService.OpenAsync();
        return await BrowserAutomationService.GetPageStateAsync();
    }

    [Description("在浏览器页面中执行自定义 JavaScript 脚本，实现高强度控制：模拟复杂交互、读取页面深层数据、修改页面状态/样式、注入事件等。脚本必须是立即返回结果的表达式或 IIFE（如 () => { ... }() 或 document.title），返回数字/布尔/null 原样、对象返回 JSON 文本、字符串自动去引号。常用例子：读取 document.title、点击某个元素 el.click()、提取表格数据 Array.from(document.querySelectorAll('tr')).map(...)、给输入框赋值并触发 input 事件。脚本执行失败会返回 ERROR 前缀的错误信息")]
    public static async Task<string> RunJsAsync(string script, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(script))
            return "错误：缺少 script 参数";
        if (!BrowserAutomationService.IsOpen)
            return "错误：浏览器未打开（请先调用 browser_open 或 browser_get_page）";
        return await BrowserAutomationService.RunJsAsync(script);
    }

    [Description("点击页面中的元素（索引来自 browser_get_page 的结果）")]
    public static async Task<string> ClickAsync(int elementIndex, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await BrowserAutomationService.ClickAsync(elementIndex);
    }

    [Description("在页面输入框中输入文本（索引来自 browser_get_page 的结果；输入后如需提交请用 browser_press 按 enter）")]
    public static async Task<string> TypeAsync(int elementIndex, string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await BrowserAutomationService.TypeAsync(elementIndex, text);
    }

    [Description("在页面元素上按键：enter=提交所在表单 / esc / tab（索引来自 browser_get_page 的结果）")]
    public static async Task<string> PressAsync(int elementIndex, string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(key)) return "错误：缺少 key 参数（enter / esc / tab）";
        return await BrowserAutomationService.PressAsync(elementIndex, key);
    }

    [Description("滚动浏览器页面：up / down / bottom / top")]
    public static async Task<string> ScrollAsync(string direction, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var d = direction.Trim().ToLowerInvariant();
        if (d is not ("up" or "down" or "bottom" or "top"))
            return "错误：direction 仅支持 up / down / bottom / top";
        return await BrowserAutomationService.ScrollAsync(d);
    }

    [Description("读取浏览器当前页面的正文文本（长文章/搜索结果详情用；约 6000 字符）")]
    public static async Task<string> GetTextAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!BrowserAutomationService.IsOpen)
            return "错误：浏览器未打开（请先调用 browser_open 或 browser_get_page）";
        return await BrowserAutomationService.GetPageTextAsync();
    }

    [Description("浏览器后退到上一页")]
    public static async Task<string> BackAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await BrowserAutomationService.BackAsync();
    }

    [Description("关闭浏览器窗口")]
    public static async Task<string> CloseAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await BrowserAutomationService.CloseAsync();
    }

    [Description("等待用户完成登录：页面处于登录拦截（跳转到 passport.jd.com 等登录地址、出现登录弹窗或「请登录」提示）时调用。系统会暂停并提示用户在浏览器窗口完成登录，用户点击「我已登录，继续」后，本工具会直接验证页面能否查到信息（登录是否真正生效）。site 填站点名（如 京东），reason 说明为什么需要登录")]
    public static async Task<string> WaitForLoginAsync(string site, string reason, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!BrowserAutomationService.IsOpen)
            return "错误：浏览器未打开（请先调用 browser_open 或 browser_get_page）";
        if (string.IsNullOrWhiteSpace(site)) site = "该网站";

        // 用户已点击「我已登录，继续」：不再依赖 cookie 检测（京东登录 cookie 是 HttpOnly，
        // document.cookie 读不到），直接验证登录是否真正生效——页面能否查到信息。
        var url = await BrowserAutomationService.RunJsAsync("location.href");
        if (BrowserLoginDetector.IsLoginWall(url))
            return $"仍在 {site} 登录页：请确认已在浏览器窗口完成登录（登录成功后页面会自动跳转），然后再次点击「我已登录，继续」。";

        // 京东场景：先验证当前页能否提取到商品，再导航到京东搜索页验证
        var isJd = site.Contains("京东") || site.Contains("jd", StringComparison.OrdinalIgnoreCase)
                   || url.Contains("jd.com", StringComparison.OrdinalIgnoreCase);
        if (!isJd)
        {
            // 其他站点：非登录墙即视为可继续（尽力而为）
            return $"已确认 {site} 不在登录拦截状态，可以继续执行。";
        }

        var countText = await BrowserAutomationService.RunJsAsync("Array.from(document.querySelectorAll('.gl-item')).length");
        if (!int.TryParse(countText, out var count) || count <= 0)
        {
            // 当前页无商品数据 → 导航到京东搜索页做权威验证
            var nav = await BrowserAutomationService.NavigateAsync("https://search.jd.com/Search?keyword=CPU");
            if (nav != "OK")
                return $"登录验证失败（导航京东搜索页失败：{nav}），请检查浏览器窗口后重试。";
            countText = await BrowserAutomationService.RunJsAsync("Array.from(document.querySelectorAll('.gl-item')).length");
            int.TryParse(countText, out count);
        }

        if (count > 0)
            return $"已确认 {site} 登录成功：页面可正常查询商品信息（提取到 {count} 个商品），可以继续查价。";

        var afterUrl = await BrowserAutomationService.RunJsAsync("location.href");
        if (BrowserLoginDetector.IsLoginWall(afterUrl))
            return $"导航后仍跳转到 {site} 登录页：登录可能未完成，请再次确认浏览器窗口中的登录状态后点击「我已登录，继续」。";
        return $"未能确认 {site} 页面可正常查询（可能触发风控或暂时无结果），请检查浏览器窗口后重试，或改用其他方式获取信息。";
    }

    private static void Add(string name, string displayName, string glyph, Delegate method,
        bool alwaysConfirm = false, string? defaultReason = null)
    {
        AgentToolRegistry.Register(new AgentTool
        {
            Name = name,
            DisplayName = displayName,
            Glyph = glyph,
            Function = AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = name }),
            AlwaysConfirm = alwaysConfirm,
            ConfirmKind = alwaysConfirm ? "login" : null,
            DefaultReason = defaultReason
        });
    }
}
