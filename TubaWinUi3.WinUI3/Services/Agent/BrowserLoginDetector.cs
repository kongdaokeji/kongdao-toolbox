namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 浏览器登录状态检测（纯逻辑，可单测）：
/// 判断当前页面是否处于登录墙（passport/login 跳转），以及站点登录 cookie 是否已写入。
/// </summary>
public static class BrowserLoginDetector
{
    /// <summary>常见登录墙主机名（命中即视为需登录）。</summary>
    private static readonly string[] LoginWallHosts =
    [
        "passport.jd.com", "login.jd.com", "plogin.jd.com",
        "passport.taobao.com", "login.taobao.com",
        "passport.bilibili.com",
        "login.microsoftonline.com", "accounts.google.com"
    ];

    /// <summary>站点登录标识 cookie（京东 pt_key 等）。HostPart 用于按站点名/URL 匹配。</summary>
    private static readonly (string HostPart, string Cookie)[] LoginCookies =
    [
        ("jd.com", "pt_key"),
        ("taobao.com", "_tb_token_"),
        ("bilibili.com", "bili_jct")
    ];

    /// <summary>是否处于登录墙：URL 命中登录主机，或主机名带 passport./login. 前缀。</summary>
    public static bool IsLoginWall(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var host = uri.Host;
            if (LoginWallHosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase)))
                return true;
            return host.Contains("passport.", StringComparison.OrdinalIgnoreCase)
                || host.StartsWith("login.", StringComparison.OrdinalIgnoreCase);
        }

        // 非绝对 URL：按关键字兜底
        return url.Contains("passport", StringComparison.OrdinalIgnoreCase)
            || url.Contains("login", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 是否已登录：URL 非登录墙，且 cookie 含站点登录标识。
    /// site 为中文/通用站点名（京东/淘宝/bilibili），也可留空按 URL 推断；
    /// 未知站点无 cookie 规则时仅按 URL 判断（非登录墙即视为可继续）。
    /// </summary>
    public static bool IsLoggedIn(string url, string cookies, string? site = null)
    {
        if (IsLoginWall(url)) return false;
        if (string.IsNullOrEmpty(cookies)) return false;

        var entry = FindCookieRule(site, url);
        if (entry is null) return true; // 未知站点：非登录墙即视为可继续
        return cookies.Contains(entry.Value.Cookie + "=", StringComparison.OrdinalIgnoreCase);
    }

    private static (string HostPart, string Cookie)? FindCookieRule(string? site, string url)
    {
        // 站点名 → 主机片段
        if (!string.IsNullOrWhiteSpace(site))
        {
            var s = site.ToLowerInvariant();
            if (s.Contains("jd") || s.Contains("京东")) return LoginCookies[0];
            if (s.Contains("taobao") || s.Contains("淘宝") || s.Contains("tmall") || s.Contains("天猫")) return LoginCookies[1];
            if (s.Contains("bilibili") || s.Contains("b站") || s.Contains("哔哩")) return LoginCookies[2];
        }

        // 按 URL 主机名推断
        if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            foreach (var rule in LoginCookies)
                if (uri.Host.Contains(rule.HostPart, StringComparison.OrdinalIgnoreCase))
                    return rule;
        }

        return null;
    }
}
