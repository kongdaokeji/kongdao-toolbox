namespace TubaWinUi3.Services;

/// <summary>
/// Default process whitelist / blacklist for EcoQoS throttling.
/// Ported from the official EnergyStarX zh-hans resources
/// (https://github.com/JasonWei512/EnergyStarX) with adjustments:
/// - the tool's own exe names whitelisted (TubaWinUi3 / tubawinui3).
/// </summary>
internal static class EnergyStarDefaults
{
    public const string DefaultProcessWhitelist =
        """
        // 每行一个进程名
        // 每行中双斜杠以及之后的内容会被忽略
        // 支持 "?" 和 "*" 通配符

        // 排除我们自己
        EnergyStar.exe
        EnergyStarX.exe
        tubawinui3.exe
        TubaWinUi3.exe
        图吧工具箱.exe
        launcher.exe
        // Edge 会自己管理好资源
        msedge.exe
        msedgewebview2.exe
        WebViewHost.exe
        WebView2.exe
        // UWP Frame 有特别处理, 不应该被限制
        ApplicationFrameHost.exe
        // 任务管理器，灭火器不可以着火
        taskmgr.exe
        procmon.exe
        procmon64.exe
        // 小组件
        Widgets.exe
        // 系统 shell
        dwm.exe
        explorer.exe
        ShellExperienceHost.exe
        StartMenuExperienceHost.exe
        SearchHost.exe
        sihost.exe
        fontdrvhost.exe
        LockApp.exe
        TabTip.exe
        WinLogon.exe
        LogonUI.exe
        // 输入法
        ChsIME.exe
        ctfmon.exe
        TextInputHost.exe
        // 系统服务 — 它们会自己管理好资源
        csrss.exe
        smss.exe
        svchost.exe
        services.exe
        lsass.exe
        wininit.exe
        winlogon.exe
        // WUDF
        WUDFRd.exe

        // Firefox 108 已支持效率模式
        firefox.exe
        """;

    public const string DefaultProcessBlacklist =
        """
        // 每行一个进程名
        // 每行中双斜杠以及之后的内容会被忽略
        // 支持 "?" 和 "*" 通配符
        // 你需要和你想要限制的进程在同一个 Windows Session 中，且拥有相同或更高的权限
        """;
}
