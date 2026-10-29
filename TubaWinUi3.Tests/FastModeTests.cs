using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TubaWinUi3.Tests;

/// <summary>
/// 低配模式（FastMode）：设置卡片 / 搜索 / 本地化接线，外加一条不变量 ——
/// UnifiedSearchService 里的每个设置项搜索键都必须同时能在设置页的两张定位表里找到，
/// 只漏一张就会出现「搜到了点不进去」（Theme 曾经就是这样漏的）。
/// </summary>
public class FastModeTests
{
    private static readonly string AppRoot = TestPaths.AppRoot;

    // ---------- 设置页 / 搜索 / 本地化接线 ----------

    [Fact]
    public void SettingsPage_ExposesFastModeCard()
    {
        var xaml = File.ReadAllText(Path.Combine(AppRoot, "Pages", "SettingsPage.xaml"));
        Assert.Contains("x:Name=\"SettingsFastModeCard\"", xaml);
        Assert.Contains("l:Uids.Uid=\"Settings_FastMode_Title\"", xaml);
        Assert.Contains("l:Uids.Uid=\"Settings_FastMode_Desc\"", xaml);
        Assert.Contains("Toggled=\"FastModeToggle_Toggled\"", xaml);
    }

    [Fact]
    public void SettingsPage_MapsFastModeToExpanderAndCard()
    {
        // 搜索/设置页定位靠这两张表，缺一个就会出现「搜到了点不进去」
        var code = File.ReadAllText(Path.Combine(AppRoot, "Pages", "SettingsPage.xaml.cs"));
        Assert.Contains("[\"FastMode\"] = \"GeneralExpander\"", code);
        Assert.Contains("[\"FastMode\"] = \"SettingsFastModeCard\"", code);
        Assert.Contains("FastModeService.SetFastModeEnabled(", code);
        // 只定义不调用的话开关状态不会同步到当前设置值（测试仍全绿，但 UI 与实际设置相反）
        Assert.Contains("InitFastModeToggle();", code);
    }

    [Fact]
    public void UnifiedSearch_ExposesFastModeSetting()
    {
        var code = File.ReadAllText(Path.Combine(AppRoot, "Services", "UnifiedSearchService.cs"));
        Assert.Contains("Search_SettingFastMode", code);
        Assert.Contains("\"FastMode\"", code);
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void Resources_ContainFastModeStrings(string language)
    {
        var path = Path.Combine(AppRoot, "Strings", language, "Resources.resw");
        Assert.True(File.Exists(path), $"缺少资源文件: {path}");

        var values = XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => e.Element("value")?.Value ?? string.Empty);

        string[] keys =
        [
            "Settings_FastMode_Title.Text",
            "Settings_FastMode_Desc.Text",
            "Search_SettingFastMode",
            "Search_SettingFastModeDesc",
        ];

        foreach (var key in keys)
        {
            Assert.True(values.TryGetValue(key, out var value), $"{language} 缺少资源键: {key}");
            Assert.False(string.IsNullOrWhiteSpace(value), $"{language} 资源键为空: {key}");
        }
    }

    // ---------- 不变量：搜到的设置项必须能定位 ----------

    /// <summary>
    /// 从 UnifiedSearchService.SettingsEntries 解析设置键，断言每一个都能在设置页的
    /// SettingKeyToExpander（展开到哪个 Expander）与 SettingKeyToCardName（定位到哪张卡片）里找到。
    /// </summary>
    [Fact]
    public void UnifiedSearch_EverySettingsEntry_IsLocatableInSettingsPage()
    {
        var searchCode = File.ReadAllText(Path.Combine(AppRoot, "Services", "UnifiedSearchService.cs"));
        var settingKeys = SettingsEntryRegex.Matches(searchCode)
            .Select(m => m.Groups[1].Value)
            .ToList();

        // 解析器本身也要有守卫：写法一变就报错，而不是静默地零断言通过
        Assert.True(settingKeys.Count > 0, "未能从 UnifiedSearchService.cs 解析出任何设置项（SettingsEntries 的写法变了？）");
        Assert.Contains("FastMode", settingKeys);

        var pageCode = File.ReadAllText(Path.Combine(AppRoot, "Pages", "SettingsPage.xaml.cs"));
        var expanderKeys = ParseDictionaryKeys(pageCode, "SettingKeyToExpander");
        var cardKeys = ParseDictionaryKeys(pageCode, "SettingKeyToCardName");
        Assert.True(expanderKeys.Count > 0, "未能从 SettingsPage.xaml.cs 解析出 SettingKeyToExpander 的键");
        Assert.True(cardKeys.Count > 0, "未能从 SettingsPage.xaml.cs 解析出 SettingKeyToCardName 的键");

        foreach (var key in settingKeys)
        {
            Assert.True(expanderKeys.Contains(key),
                $"设置项 \"{key}\" 能搜到，但 SettingKeyToExpander 里没有它 —— 搜索结果无法展开到对应区块");
            Assert.True(cardKeys.Contains(key),
                $"设置项 \"{key}\" 能搜到，但 SettingKeyToCardName 里没有它 —— 搜索结果无法定位到设置卡片");
        }
    }

    private static readonly Regex SettingsEntryRegex = new(
        @"new\(""Search_Setting\w+"",\s*""[^""]*"",\s*""Search_Setting\w+"",\s*""[^""]*"",\s*""\\u[0-9A-Fa-f]{4}"",\s*""([A-Za-z0-9_]+)""\)",
        RegexOptions.Compiled);

    /// <summary>取出 <c>字典名 = new(...) { ... };</c> 块里的全部键。</summary>
    private static HashSet<string> ParseDictionaryKeys(string code, string dictionaryName)
    {
        var start = code.IndexOf($"{dictionaryName} = new(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"SettingsPage.xaml.cs 里找不到字典 {dictionaryName}");

        var end = code.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(end > start, $"字典 {dictionaryName} 的结束标记 }}; 缺失");

        return Regex.Matches(code[start..end], @"\[""([A-Za-z0-9_]+)""\]\s*=")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }
}
