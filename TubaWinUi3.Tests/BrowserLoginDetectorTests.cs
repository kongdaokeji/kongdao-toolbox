using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

/// <summary>浏览器登录状态检测（纯逻辑）测试。</summary>
public class BrowserLoginDetectorTests
{
    [Theory]
    [InlineData("https://passport.jd.com/new/login.aspx?ReturnUrl=item.jd.com", true)]
    [InlineData("https://login.jd.com/?appid=xx", true)]
    [InlineData("https://plogin.jd.com/login/account", true)]
    [InlineData("https://passport.taobao.com/acc/login.htm", true)]
    [InlineData("https://passport.bilibili.com/login", true)]
    [InlineData("https://search.jd.com/Search?keyword=cpu", false)]
    [InlineData("https://item.jd.com/100012345678.html", false)]
    [InlineData("https://www.jd.com/", false)]
    [InlineData("https://login.microsoftonline.com/", true)]
    public void IsLoginWall_DetectsHosts(string url, bool expected)
        => Assert.Equal(expected, BrowserLoginDetector.IsLoginWall(url));

    [Fact]
    public void IsLoginWall_NonUrlWithLoginKeyword_True()
        => Assert.True(BrowserLoginDetector.IsLoginWall("跳转到 passport.jd.com 登录"));

    [Fact]
    public void IsLoggedIn_JdCookiePresent_True()
    {
        var loggedIn = BrowserLoginDetector.IsLoggedIn(
            "https://item.jd.com/100012.html", "pt_key=abc123; pt_pin=user; ", "京东");
        Assert.True(loggedIn);
    }

    [Fact]
    public void IsLoggedIn_JdCookieMissing_False()
    {
        var loggedIn = BrowserLoginDetector.IsLoggedIn(
            "https://item.jd.com/100012.html", "other_cookie=1; ", "京东");
        Assert.False(loggedIn);
    }

    [Fact]
    public void IsLoggedIn_LoginWallUrl_False_EvenWithCookie()
    {
        var loggedIn = BrowserLoginDetector.IsLoggedIn(
            "https://passport.jd.com/new/login.aspx", "pt_key=abc123; ", "京东");
        Assert.False(loggedIn);
    }

    [Fact]
    public void IsLoggedIn_NoCookies_False()
    {
        var loggedIn = BrowserLoginDetector.IsLoggedIn("https://item.jd.com/100012.html", "", "京东");
        Assert.False(loggedIn);
    }

    [Fact]
    public void IsLoggedIn_SiteInferredFromUrl_WhenSiteEmpty()
    {
        // site 留空：按 URL 推断 jd.com → 要求 pt_key
        Assert.True(BrowserLoginDetector.IsLoggedIn("https://item.jd.com/1.html", "pt_key=abc"));
        Assert.False(BrowserLoginDetector.IsLoggedIn("https://item.jd.com/1.html", "other=1"));
    }

    [Fact]
    public void IsLoggedIn_TaobaoUsesTokenCookie()
    {
        Assert.True(BrowserLoginDetector.IsLoggedIn(
            "https://item.taobao.com/item.htm?id=1", "_tb_token_=xyz; ", "淘宝"));
        Assert.False(BrowserLoginDetector.IsLoggedIn(
            "https://item.taobao.com/item.htm?id=1", "pt_key=abc; ", "淘宝"));
    }

    [Fact]
    public void IsLoggedIn_UnknownSite_NoLoginWall_True()
    {
        // 未知站点无 cookie 规则：非登录墙即视为可继续
        Assert.True(BrowserLoginDetector.IsLoggedIn(
            "https://www.example.com/page", "whatever=1", "示例站"));
    }
}
