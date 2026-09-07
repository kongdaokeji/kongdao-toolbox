using TubaWinUi3.Services.RogueCleaner;

namespace TubaWinUi3.Tests.RogueCleaner;

/// <summary>「流氓软件的克星」核心模型与纯逻辑测试。</summary>
public class RogueCleanerCoreTests
{
    [Fact]
    public void ChineseDisplayText_TranslatesKnownMenus()
    {
        Assert.Equal("打开", ChineseDisplayText.ContextMenuName("Open"));
        Assert.Equal("使用 Microsoft Defender 扫描", ChineseDisplayText.ContextMenuName("Scan with Microsoft Defender..."));
        Assert.Equal("作为 PyCharm 项目打开文件夹", ChineseDisplayText.ContextMenuName("Open Folder as PyCharm Project"));
        Assert.Equal("在此处打开 Git Bash", ChineseDisplayText.ContextMenuName("Open Git Bash here"));
        Assert.Equal("发送到 target", ChineseDisplayText.ContextMenuName("Send to target"));
    }

    [Fact]
    public void ChineseDisplayText_KeepsChineseUntouched()
    {
        Assert.Equal("打开文件夹", ChineseDisplayText.ContextMenuName("打开文件夹"));
        Assert.True(ChineseDisplayText.HasChinese("中文"));
        Assert.False(ChineseDisplayText.HasChinese("English only"));
    }

    [Fact]
    public void ChineseDisplayText_CleanupStatus_MapsAllStates()
    {
        Assert.Equal("已处理", ChineseDisplayText.CleanupStatus("Done"));
        Assert.Equal("失败", ChineseDisplayText.CleanupStatus("Failed"));
        Assert.Equal("恢复失败", ChineseDisplayText.CleanupStatus("RestoreFailed"));
        Assert.Equal("已打开卸载窗口", ChineseDisplayText.CleanupStatus("Launched"));
        Assert.Equal("已跳过", ChineseDisplayText.CleanupStatus("Skipped"));
        Assert.Equal("未知", ChineseDisplayText.CleanupStatus(""));
        // 中文状态原样返回（右键菜单条目等直接使用中文状态）
        Assert.Equal("已启用", ChineseDisplayText.CleanupStatus("已启用"));
    }

    [Fact]
    public void Finding_RiskDisplay_ReportOnlyShowsHint()
    {
        var cleanable = new Finding { Risk = "高", ActionKind = "DeleteRegistryKey" };
        Assert.True(cleanable.CanClean);
        Assert.Equal("高", cleanable.RiskDisplay);
        Assert.True(cleanable.BulkSelectable);

        var reportOnly = new Finding { Risk = "高", ActionKind = "ReportOnly" };
        Assert.False(reportOnly.CanClean);
        Assert.Equal("仅提示", reportOnly.RiskDisplay);
        Assert.False(reportOnly.BulkSelectable);
    }

    [Fact]
    public void Finding_CompactFields_Summarize()
    {
        var f = new Finding
        {
            UserVisibleName = "普通文件右键“某某广告组件”会出现广告菜单",
            Category = "右键菜单",
            UserImpact = "在文件右键菜单显示广告入口。",
            ActionKind = "DisableShellExtension"
        };
        Assert.Contains("某某广告组件", f.CompactTitle);
        Assert.Equal("文件右键", f.CompactLocation);
        Assert.Equal("右键入口", f.CompactImpact);
        Assert.Equal("备份禁用", f.CompactAction);
    }

    [Fact]
    public void ProductRemovalPolicy_ClassifiesIndependentProducts()
    {
        var disposition = ProductRemovalPolicy.Classify(
            displayName: "360桌面助手",
            childName: "desktop",
            installLocation: @"C:\Program Files\360desktop",
            displayIcon: "desk.exe",
            uninstallCommand: "uninstall.exe",
            hidden: true,
            adOrGuard: true,
            badComponent: true);
        Assert.Equal(ProductRemovalDisposition.TargetIndependentProduct, disposition);
    }

    [Fact]
    public void UserWhitelistStore_Roundtrip_AddLoadRemove()
    {
        var store = TempStore();
        var finding = new Finding
        {
            UserVisibleName = "测试白名单项",
            ActionKind = "DeleteRegistryKey",
            Target = new ActionTarget { Kind = "DeleteRegistryKey", Hive = "HKCU", View = "Registry64", SubKey = @"Software\Test\Key", ValueName = "" }
        };

        Assert.Empty(UserWhitelistStore.Load(store));
        Assert.True(UserWhitelistStore.Add(store, finding));
        Assert.False(UserWhitelistStore.Add(store, finding), "重复添加应返回 false");
        Assert.Single(UserWhitelistStore.Load(store));
        Assert.True(UserWhitelistStore.Remove(store, finding));
        Assert.Empty(UserWhitelistStore.Load(store));
    }

    [Fact]
    public void UserWhitelistStore_Apply_MarksReportOnly()
    {
        var store = TempStore();
        var finding = new Finding
        {
            UserVisibleName = "已白名单项",
            Risk = "高",
            ActionKind = "DeleteRegistryValue",
            Target = new ActionTarget { Kind = "DeleteRegistryValue", Hive = "HKCU", View = "Default", SubKey = @"Software\Test", ValueName = "Run" }
        };
        UserWhitelistStore.Add(store, finding);
        UserWhitelistStore.Apply(store, new[] { finding });
        Assert.False(finding.Selected);
        Assert.Equal("ReportOnly", finding.ActionKind);
        Assert.Equal("已白名单", finding.Status);
        Assert.Equal("低", finding.Risk);
    }

    [Fact]
    public void ContextMenuDiagnosisPolicy_ProtectsSystemCommands()
    {
        var identity = new VendorIdentityResult { Vendor = "第三方", Confidence = 80, Confirmed = true, Conflicted = false };
        var entry = new ContextMenuEntry
        {
            Scene = "所有文件",
            Type = "Shell 命令",
            Enabled = true,
            Command = @"%SystemRoot%\system32\windowsdefender.dll, -RunAsTrustedInstaller",
            SubKey = @"Software\Classes\*\shell\roguewindowsdefender"
        };
        Assert.Equal(ContextMenuDiagnosisDisposition.SystemProtected, ContextMenuDiagnosisPolicy.Classify(entry, identity));
    }

    [Fact]
    public void ContextMenuDiagnosisPolicy_ActionableThirdPartyCommand()
    {
        var identity = new VendorIdentityResult { Vendor = "某流氓软件", Confidence = 90, Confirmed = true, Conflicted = false };
        var entry = new ContextMenuEntry
        {
            Scene = "所有文件",
            Type = "Shell 命令",
            Enabled = true,
            Command = @"C:\Program Files\RogueSoft\ads.exe ""%1""",
            SubKey = @"Software\Classes\*\shell\rogueads"
        };
        Assert.Equal(ContextMenuDiagnosisDisposition.ActionableCommand, ContextMenuDiagnosisPolicy.Classify(entry, identity));
    }

    private static DataStore TempStore()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TubaWinUi3-RogueCleaner-Tests", Guid.NewGuid().ToString("N"));
        var store = DataStore.CreateForExecutable(Path.Combine(dir, "app.exe"));
        store.Ensure();
        return store;
    }
}
