using TubaWinUi3.Services.RogueCleaner;

namespace TubaWinUi3.Tests.RogueCleaner;

/// <summary>厂商识别规则库自测（Authenticode/MSI 识别、证据评分）。</summary>
public class RogueCleanerRuleCatalogTests
{
    [Fact]
    public void RuleCatalog_RunIdentitySelfTests_AllPass()
    {
        // 原版内置的识别回归自测：识别规则、厂商评分、身份冲突等。
        var failures = RuleCatalog.RunIdentitySelfTests();
        Assert.True(failures.Count == 0, "规则库自测失败：\n" + string.Join("\n", failures));
    }

    [Fact]
    public void RuleCatalog_HasBadComponent_DetectsKnownBadTokens()
    {
        // Safe360Ext 属于 360 规则的 BadComponents（广告/守护组件）
        Assert.True(RuleCatalog.HasBadComponent("Safe360Ext"));
        Assert.False(RuleCatalog.HasBadComponent("普通记事本 Notepad"));
    }

    [Fact]
    public void RuleCatalog_ResolveIdentity_NeedsEvidence()
    {
        var identity = RuleCatalog.ResolveIdentity(new VendorEvidence());
        Assert.False(identity.Confirmed);
    }

    [Fact]
    public void ChineseDisplayText_SoftwareName_KeepsChineseAndMapsKnown()
    {
        Assert.Equal("WPS / 金山", ChineseDisplayText.SoftwareName("WPS Office"));
        Assert.Equal("来源未确认", ChineseDisplayText.SoftwareName(""));
        // 含中文的名称原样返回
        Assert.Equal("某工具", ChineseDisplayText.SoftwareName("某工具"));
    }
}
