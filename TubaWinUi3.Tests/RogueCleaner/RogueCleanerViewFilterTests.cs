using TubaWinUi3.Services.RogueCleaner;

namespace TubaWinUi3.Tests.RogueCleaner;

/// <summary>「流氓软件的克星」总览页筛选谓词测试（列表底部「展示全部」显示被隐藏条目）。</summary>
public class RogueCleanerViewFilterTests
{
    [Fact]
    public void MatchesStartupTab_OnlyMatchesStartupRelatedCategories()
    {
        Assert.True(RogueCleanerViewFilters.MatchesStartupTab(new Finding { Category = "启动项" }));
        Assert.True(RogueCleanerViewFilters.MatchesStartupTab(new Finding { Category = "计划任务" }));
        Assert.True(RogueCleanerViewFilters.MatchesStartupTab(new Finding { Category = "后台服务" }));
        Assert.False(RogueCleanerViewFilters.MatchesStartupTab(new Finding { Category = "右键菜单" }));
        Assert.False(RogueCleanerViewFilters.MatchesStartupTab(new Finding { Category = "文件关联" }));
        Assert.False(RogueCleanerViewFilters.MatchesStartupTab(new Finding { Category = null }));
    }

    [Fact]
    public void MatchesPopupTab_OnlyMatchesPopupRelatedCategories()
    {
        Assert.True(RogueCleanerViewFilters.MatchesPopupTab(new Finding { Category = "正在运行" }));
        Assert.True(RogueCleanerViewFilters.MatchesPopupTab(new Finding { Category = "弹窗" }));
        Assert.True(RogueCleanerViewFilters.MatchesPopupTab(new Finding { Category = "守护进程" }));
        Assert.True(RogueCleanerViewFilters.MatchesPopupTab(new Finding { Category = "捆绑安装" }));
        Assert.False(RogueCleanerViewFilters.MatchesPopupTab(new Finding { Category = "右键菜单" }));
        Assert.False(RogueCleanerViewFilters.MatchesPopupTab(new Finding { Category = "" }));
    }

    [Fact]
    public void MatchesMainMenuList_ShowsResolvedThirdPartyAndUserAdded()
    {
        Assert.True(RogueCleanerViewFilters.MatchesMainMenuList(new ContextMenuEntry
        {
            AdvancedOnly = false,
            PresentationResolved = true,
            IsThirdParty = true
        }));
        Assert.True(RogueCleanerViewFilters.MatchesMainMenuList(new ContextMenuEntry
        {
            AdvancedOnly = false,
            PresentationResolved = true,
            UserAdded = true
        }));
    }

    [Fact]
    public void MatchesMainMenuList_HidesSystemUnresolvedAndAdvancedOnly()
    {
        Assert.False(RogueCleanerViewFilters.MatchesMainMenuList(new ContextMenuEntry
        {
            AdvancedOnly = false,
            PresentationResolved = true,
            IsThirdParty = false
        }));
        Assert.False(RogueCleanerViewFilters.MatchesMainMenuList(new ContextMenuEntry
        {
            AdvancedOnly = false,
            PresentationResolved = false,
            IsThirdParty = true
        }));
        Assert.False(RogueCleanerViewFilters.MatchesMainMenuList(new ContextMenuEntry
        {
            AdvancedOnly = true,
            PresentationResolved = true,
            IsThirdParty = true
        }));
    }

    [Fact]
    public void MatchesKeyword_ContextMenuEntry_MatchesNameCommandAndLocation()
    {
        var entry = new ContextMenuEntry
        {
            Name = "上传到 WPS 云文档",
            SoftwareName = "WPS Office",
            Scope = "所有用户 / 64 位",
            Command = "\"C:\\Program Files\\WPS\\wps.exe\" \"%1\"",
            Clsid = "{11111111-2222-3333-4444-555555555555}",
            SubKey = @"Software\Classes\*\shell\WPS"
        };
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(entry, "wps"));
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(entry, "云文档"));
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(entry, "wps.exe"));
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(entry, "64 位"));
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(entry, "11111111"));
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(entry, "WPS"));
        Assert.False(RogueCleanerViewFilters.MatchesKeyword(entry, "不存在关键字"));
    }

    [Fact]
    public void MatchesKeyword_SpecialAndAdvancedEntries_MatchDetailAndModuleFields()
    {
        var special = new SpecialMenuEntry
        {
            Name = ".txt 新建文档",
            Detail = "FileName=template.txt",
            Scope = "当前用户 / 32 位",
            SubKey = @"Software\Classes\.txt\ShellNew"
        };
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(special, "template"));
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(special, "32 位"));
        Assert.False(RogueCleanerViewFilters.MatchesKeyword(special, "wps"));

        var advanced = new AdvancedMenuEntry
        {
            Name = "Windows 终端",
            Detail = "{12345678-1234-1234-1234-123456789012} / * / 现代",
            Scope = "当前用户 / Windows 11",
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked",
            FilePath = @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal\terminal.exe"
        };
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(advanced, "terminal"));
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(advanced, "12345678"));
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(advanced, "WindowsApps"));
        Assert.False(RogueCleanerViewFilters.MatchesKeyword(advanced, "wps"));
    }

    [Fact]
    public void MatchesKeyword_EmptyKeyword_MatchesEverything()
    {
        var entry = new ContextMenuEntry { Name = "任意菜单", SoftwareName = null, Command = null, Clsid = null, SubKey = null };
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(entry, ""));
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(entry, "   "));
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(new SpecialMenuEntry { Name = "x" }, null));
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(new AdvancedMenuEntry { Name = "x" }, ""));
    }

    [Fact]
    public void MatchesKeyword_NullFields_DoNotThrow()
    {
        var entry = new ContextMenuEntry { Name = "测试" };
        Assert.True(RogueCleanerViewFilters.MatchesKeyword(entry, "测试"));
        Assert.False(RogueCleanerViewFilters.MatchesKeyword(entry, "其它"));
    }
}
