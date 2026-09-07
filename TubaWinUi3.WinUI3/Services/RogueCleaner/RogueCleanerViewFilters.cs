using System;
using System.Linq;

namespace TubaWinUi3.Services.RogueCleaner;

/// <summary>「流氓软件的克星」各总览页的列表筛选谓词;页面与单元测试共用。</summary>
internal static class RogueCleanerViewFilters
{
    /// <summary>启动项管理 tab:只显示启动相关类别(启动/计划任务/后台服务)。</summary>
    public static bool MatchesStartupTab(Finding f)
        => ContainsAny(f.Category, "启动", "计划任务", "后台服务");

    /// <summary>弹窗/流氓诊断 tab:只显示弹窗与守护相关类别(正在运行/弹窗/守护/捆绑)。</summary>
    public static bool MatchesPopupTab(Finding f)
        => ContainsAny(f.Category, "正在运行", "弹窗", "守护", "捆绑");

    /// <summary>右键菜单主列表默认显示:已识别第三方 + 用户自建,排除系统内置、未识别与技术记录。</summary>
    public static bool MatchesMainMenuList(ContextMenuEntry e)
        => !e.AdvancedOnly && e.PresentationResolved && (e.IsThirdParty || e.UserAdded);

    /// <summary>右键菜单管理搜索关键字匹配（名称/软件/命令/位置/组件编号等，大小写不敏感）。</summary>
    public static bool MatchesKeyword(ContextMenuEntry e, string keyword)
        => Matches(e.Name, keyword) || Matches(e.RawName, keyword) || Matches(e.SoftwareName, keyword)
        || Matches(e.Scene, keyword) || Matches(e.Scope, keyword) || Matches(e.Command, keyword)
        || Matches(e.Clsid, keyword) || Matches(e.SubKey, keyword);

    /// <summary>专用模块（新建/发送到/打开方式/组件屏蔽）搜索关键字匹配。</summary>
    public static bool MatchesKeyword(SpecialMenuEntry e, string keyword)
        => Matches(e.Name, keyword) || Matches(e.Detail, keyword) || Matches(e.Scope, keyword) || Matches(e.SubKey, keyword);

    /// <summary>高级模块（WinX/现代菜单/IE/安全增强）搜索关键字匹配。</summary>
    public static bool MatchesKeyword(AdvancedMenuEntry e, string keyword)
        => Matches(e.Name, keyword) || Matches(e.Detail, keyword) || Matches(e.Scope, keyword)
        || Matches(e.SubKey, keyword) || Matches(e.FilePath, keyword);

    /// <summary>大小写不敏感子串匹配；关键字为空（含空白）时不参与过滤，视为全部匹配。</summary>
    private static bool Matches(string? value, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return value != null && value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsAny(string? value, params string[] needles)
        => value != null && needles.Any(n => value.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
}
