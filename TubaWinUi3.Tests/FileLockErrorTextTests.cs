using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TubaWinUi3.Pages;
using TubaWinUi3.Services.FileLock;

namespace TubaWinUi3.Tests;

/// <summary>
/// 扫描失败详情的本地化。服务层以前直接产中文散文（「目标不是磁盘上的文件或目录」），
/// 拼进英文模板就成了「Could not resolve this path (目标不是磁盘上的文件或目录)」这种混语提示。
/// 约定是服务产原因码、显示层翻译——这里钉住两件事：每个原因码都有非空文案；
/// FileLockErrorText 引用的 resw 键在中英两份资源里都存在。
/// </summary>
public class FileLockErrorTextTests
{
    private static readonly string AppRoot = Path.Combine(FindRepoRoot(), "TubaWinUi3.WinUI3");

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

        return XDocument.Load(path).Root!.Elements("data").ToDictionary(
            e => (string?)e.Attribute("name") ?? "",
            e => e.Element("value")?.Value ?? "");
    }

    [Fact]
    public void EveryFailureReason_HasNonEmptyText()
    {
        foreach (FileLockScanFailure failure in Enum.GetValues<FileLockScanFailure>())
        {
            if (failure == FileLockScanFailure.None) continue;   // 没有失败就没有详情可显示

            Assert.False(string.IsNullOrWhiteSpace(FileLockErrorText.Describe(failure, code: 5)),
                $"失败原因 {failure} 没有可显示的文案");
        }
    }

    [Fact]
    public void ErrorTextKeys_ExistInBothLanguages()
    {
        string source = File.ReadAllText(Path.Combine(AppRoot, "Pages", "FileLockErrorText.cs"));
        var keys = Regex.Matches(source, "L\\(\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        // 扫描面自检：正则失灵时不要静默通过
        Assert.True(keys.Count >= 6, $"只扫到 {keys.Count} 个键，检查方式可能已失效");

        var zh = LoadResw("zh-CN");
        var en = LoadResw("en-US");
        foreach (string key in keys)
        {
            Assert.True(zh.ContainsKey(key), $"zh-CN 缺少键: {key}");
            Assert.True(en.ContainsKey(key), $"en-US 缺少键: {key}");

            // 空值会让 LocalizationService.L 回退到中文 fallback——英文界面静默变中文
            Assert.False(string.IsNullOrWhiteSpace(zh[key]), $"zh-CN 的 {key} 是空值");
            Assert.False(string.IsNullOrWhiteSpace(en[key]), $"en-US 的 {key} 是空值");
        }
    }
}
