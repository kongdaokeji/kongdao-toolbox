using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>界面本地化资源一致性：resw 键集合、XAML Uid 覆盖、分类显示名映射。</summary>
public class LocalizationTests
{
    private static readonly string AppRoot = Path.Combine(FindRepoRoot(), "TubaWinUi3.WinUI3");

    /// <summary>
    /// 向上查找 TubaWinUi3.sln 定位仓库根。优先用编译期嵌入的源码文件路径，
    /// 这样即使构建输出到备用目录（BaseOutputPath）也能定位。
    /// </summary>
    private static string FindRepoRoot([CallerFilePath] string callerFilePath = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(callerFilePath), AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(start)) continue;

            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TubaWinUi3.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException("未找到仓库根目录（TubaWinUi3.sln）");
    }

    private static Dictionary<string, string> LoadResw(string language)
    {
        var path = Path.Combine(AppRoot, "Strings", language, "Resources.resw");
        Assert.True(File.Exists(path), $"缺少资源文件: {path}");

        var doc = XDocument.Load(path);
        var entries = doc.Root!.Elements("data").ToList();

        var duplicates = entries
            .GroupBy(e => (string?)e.Attribute("name"))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.Empty(duplicates);

        return entries.ToDictionary(
            e => (string)e.Attribute("name")!,
            e => e.Element("value")?.Value ?? string.Empty);
    }

    [Fact]
    public void ReswFiles_HaveIdenticalKeysAndNonEmptyValues()
    {
        var zh = LoadResw("zh-CN");
        var en = LoadResw("en-US");

        Assert.Empty(zh.Keys.Except(en.Keys).ToList());
        Assert.Empty(en.Keys.Except(zh.Keys).ToList());
        Assert.All(zh, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"空值: {kv.Key}"));
        Assert.All(en, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"空值: {kv.Key}"));
    }

    // 附加属性形式会使 WinUI3Localizer 扫描所有程序集的 GetTypes()，
    // 提前解析尚未下载的 Windows ML 类型。提示文字统一走 Button 的自定义动作。
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void ReswKeys_DoNotTriggerAttachedPropertyAssemblyScan(string language)
    {
        var keys = LoadResw(language).Keys;
        Assert.Empty(keys.Where(key => key.Count(c => c == '.') > 1));
        Assert.Contains("Settings_HttpDownloadResetPath_Button.ToolTip", keys);
    }

    [Fact]
    public void XamlUids_AreDefinedInChineseResources()
    {
        var resourceKeys = LoadResw("zh-CN").Keys.ToHashSet(StringComparer.Ordinal);
        var uidPattern = new Regex("l:Uids\\.Uid=\"([^\"]+)\"", RegexOptions.Compiled);

        var xamlFiles = Directory.EnumerateFiles(AppRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        var missing = new List<string>();
        var seenUids = 0;
        foreach (var file in xamlFiles)
        {
            foreach (Match match in uidPattern.Matches(File.ReadAllText(file)))
            {
                seenUids++;
                var uid = match.Groups[1].Value;
                if (!resourceKeys.Any(k => k == uid || k.StartsWith(uid + ".", StringComparison.Ordinal)))
                    missing.Add($"{Path.GetFileName(file)}: {uid}");
            }
        }

        Assert.True(seenUids > 0, "未扫描到任何 Uid（测试路径可能不对）");
        Assert.Empty(missing);
    }

    [Fact]
    public void CategoryDisplayNames_CoverKnownCategories()
    {
        var resourceKeys = LoadResw("zh-CN").Keys.ToHashSet(StringComparer.Ordinal);

        string[] toolCategories =
        [
            "处理器工具", "显卡工具", "显示器工具", "硬盘工具", "内存工具", "外设工具",
            "游戏工具", "声卡工具", "网卡工具", "烤鸡工具", "综合检测", "综合工具", "其他工具",
        ];
        string[] builtinCategories = ["系统工具", "硬件工具", "网络工具", "游戏工具", "实用工具"];

        foreach (var category in toolCategories)
        {
            Assert.True(CategoryDisplayNames.TryGetToolCategoryKey(category, out var key), $"缺少映射: {category}");
            Assert.Contains(key, resourceKeys);
        }

        foreach (var category in builtinCategories)
        {
            Assert.True(CategoryDisplayNames.TryGetBuiltinCategoryKey(category, out var key), $"缺少映射: {category}");
            Assert.Contains(key, resourceKeys);
        }

        // 未收录的自定义目录沿用「去掉『工具』后缀」的原有行为
        Assert.Equal("我的自定义", LocalizationService.GetCategoryDisplayName("我的自定义工具"));
        Assert.False(CategoryDisplayNames.TryGetToolCategoryKey("未知目录", out _));
    }
}
