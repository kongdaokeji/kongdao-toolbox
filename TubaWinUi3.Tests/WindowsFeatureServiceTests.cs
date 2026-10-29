using System.IO;
using System.Runtime.InteropServices;
using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// Windows 隐藏功能（ViVe 移植）单元测试：CompactState 位域打包、功能 ID 混淆、
/// 不可变优先级校验、结构布局。字典解析与 /query 语义对照。
/// </summary>
public class WindowsFeatureServiceTests
{
    // ───────────────────────────── 结构布局 ─────────────────────────────

    [Fact]
    public void StructLayouts_MatchWindows()
    {
        // RtlFeatureConfiguration: 3 × uint = 12
        Assert.Equal(12, System.Runtime.InteropServices.Marshal.SizeOf<ProbeConfig>());
        // RtlFeatureConfigurationUpdate: 8 × uint = 32
        Assert.Equal(32, System.Runtime.InteropServices.Marshal.SizeOf<ProbeUpdate>());
    }

    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProbeConfig { public uint A; public uint B; public uint C; }

    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProbeUpdate { public uint A, B, C, D, E, F, G, H; }

    // ───────────────────────────── CompactState 位域 ─────────────────────────────

    [Theory]
    [InlineData(0u, 0, 0, false)]
    [InlineData(2u << 4, 0, 2, false)]                      // 单独 Enabled
    [InlineData((uint)(8 | (2 << 4)), 8, 2, false)]         // User + Enabled
    [InlineData((uint)(8 | (2 << 4) | (1 << 6)), 8, 2, true)] // User + Enabled + Wexp
    [InlineData((uint)(2 | (1 << 4) | (33 << 8)), 2, 1, false)] // Disabled + Variant 33
    [InlineData((uint)(15 | (63 << 8)), 15, 0, false)]      // 边界：priority 15 / variant 63
    public void CompactState_BitPacking(uint compact, int expectedPriority, int expectedState, bool expectedWexp)
    {
        var state = new ProbeCompact { CompactState = compact };
        Assert.Equal(expectedPriority, state.Priority);
        Assert.Equal(expectedState, state.EnabledState);
        Assert.Equal(expectedWexp, state.IsWexp);
    }

    [Fact]
    public void CompactState_VariantAndPayloadKind()
    {
        var state = new ProbeCompact { CompactState = (uint)(33 << 8 | 2 << 14) };
        Assert.Equal(33, state.Variant);
        Assert.Equal(2, state.VariantPayloadKind);
    }

    private struct ProbeCompact
    {
        public uint CompactState;
        public int Priority => (int)(CompactState & 0xF);
        public int EnabledState => (int)((CompactState >> 4) & 0x3);
        public bool IsWexp => ((CompactState >> 6) & 1) == 1;
        public int Variant => (int)((CompactState >> 8) & 0x3F);
        public int VariantPayloadKind => (int)((CompactState >> 14) & 0x3);
    }

    // ───────────────────────────── 功能 ID 混淆 ─────────────────────────────

    [Fact]
    public void FeatureId_ObfuscationRoundtrip()
    {
        foreach (var id in new uint[] { 0, 1, 0x12345678, 0xDEADBEEF, 999999999, uint.MaxValue })
            Assert.Equal(id, WindowsFeatureService.DeobfuscateFeatureId(WindowsFeatureService.ObfuscateFeatureId(id)));
    }

    /// <summary>已知向量：按 ViVe ObfuscationHelpers.cs 的 C# 语义手工推算 obfuscate(0x12345678) = 0xF0C296AC。</summary>
    [Fact]
    public void FeatureId_ObfuscationKnownVector()
    {
        Assert.Equal(0xF0C2_96ACu, WindowsFeatureService.ObfuscateFeatureId(0x1234_5678));
        Assert.Equal(0x1234_5678u, WindowsFeatureService.DeobfuscateFeatureId(0xF0C2_96AC));
    }

    // ───────────────────────────── 不可变优先级 ─────────────────────────────

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(3u)]
    [InlineData(9u)]
    [InlineData(15u)]
    public void ImmutablePriorities_RejectedByValidate(uint priority)
    {
        Assert.Contains(priority, WindowsFeatureService.ImmutablePriorities);
        Assert.Throws<InvalidOperationException>(() => WindowsFeatureService.Reset(1, priority));
    }

    // ───────────────────────────── 功能字典解析 ─────────────────────────────

    [Fact]
    public void LoadDictionary_ParsesNameIdPairs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wf_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "FeatureDictionary.pfs");
            File.WriteAllText(path, """
                PreventTaskbarPins,55394562
                TaskbarPinOnShiftRightClick,56041482

                InvalidLineWithoutComma
                ,12345678

                DesktopSpotlightImprovementsXRFixes,56201342
                """);

            var map = WindowsFeatureService.LoadDictionaryFromFile(path);

            Assert.Equal(3, map.Count);
            Assert.Equal("PreventTaskbarPins", map[55394562u]);
            Assert.Equal("TaskbarPinOnShiftRightClick", map[56041482u]);
            Assert.Equal("DesktopSpotlightImprovementsXRFixes", map[56201342u]);
            Assert.False(map.ContainsKey(12345678u));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadDictionary_DuplicateIdKeepsFirst()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wf_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "FeatureDictionary.pfs");
            File.WriteAllText(path, "First,100\nSecond,100\n");

            var map = WindowsFeatureService.LoadDictionaryFromFile(path);

            Assert.Single(map);
            Assert.Equal("First", map[100u]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadDictionary_MissingFile_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid().ToString("N") + ".pfs");
        Assert.Throws<FileNotFoundException>(() => WindowsFeatureService.LoadDictionaryFromFile(missing));
    }

    // ───────────────────────────── 优先级可读文案 ─────────────────────────────

    [Fact]
    public void PriorityText_MapsKnownPriorities()
    {
        Assert.Equal("Service", new FeatureFlagEntry(1, null, 4, FeatureState.Enabled, false, true).PriorityText);
        Assert.Equal("User", new FeatureFlagEntry(1, null, 8, FeatureState.Enabled, false, true).PriorityText);
        Assert.Equal("ImageOverride", new FeatureFlagEntry(1, null, 15, FeatureState.Enabled, false, true).PriorityText);
        Assert.Equal("ImageDefault", new FeatureFlagEntry(1, null, 0, FeatureState.Enabled, false, true).PriorityText);
        // 未知优先级回退数字形式
        Assert.Equal("Priority 7", new FeatureFlagEntry(1, null, 7, FeatureState.Enabled, false, true).PriorityText);
    }
}