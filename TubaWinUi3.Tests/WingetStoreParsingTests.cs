using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class WingetStoreParsingTests
{
    // 真实 winget search "chrome" 输出（v1.29，中文系统）
    private const string RealSearchOutput = """
        名称                                                  ID                                 版本           匹配
        -----------------------------------------------------------------------------------------------------------------------------
        Google Chrome                                         Google.Chrome                      152.0.7977.65  Moniker: chrome
        Dichromate                                            Dichromate.Browser                 111.0.5563.65  Command: chrome
        Browser Bookmarks for PowerToys Command Palette       GOODJINC.CmdPalBrowserBookmarks    0.2.3          Tag: chrome
        DataLimiter                                           MichaelSam94.DataLimiter           0.3.2          Tag: chrome

        """;

    private const string RealSearchOutputCrlf = "名称                                                  ID                                 版本           匹配\r\n" +
        "-----------------------------------------------------------------------------------------------------------------------------\r\n" +
        "Google Chrome                                         Google.Chrome                      152.0.7977.65  Moniker: chrome\r\n" +
        "Dichromate                                            Dichromate.Browser                 111.0.5563.65  Command: chrome\r\n" +
        "\r\n";

    // 真实 winget show --id Google.Chrome 输出（中文系统，全角冒号）
    private const string RealShowOutput = """
        已找到 Google Chrome [Google.Chrome]
        版本: 152.0.7977.65
        发布者: Google LLC
        安装：
          安装程序类型： wix
          安装程序 URL： https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi
          安装程序 SHA256： f3b164ba3d3ab9a76b6e93abae3dd89e042fe36655cb58c45504992925a87fe4
          发布日期: 2026-08-20
          支持脱机分发: true

        """;

    [Fact]
    public void ParseWingetSearchOutput_RealChineseOutput_ParsesColumns()
    {
        var results = WingetStoreService.ParseWingetSearchOutput(RealSearchOutput);

        Assert.Equal(4, results.Count);
        Assert.Contains(results, r => r.PackageIdentifier == "Google.Chrome");
        Assert.Contains(results, r => r.PackageName == "Google Chrome");
        Assert.Contains(results, r => r.PackageIdentifier == "Dichromate.Browser");
        Assert.Contains(results, r => r.PackageName == "Dichromate");
        Assert.Contains(results, r => r.PackageIdentifier == "GOODJINC.CmdPalBrowserBookmarks");
        Assert.Contains(results, r => r.PackageName == "Browser Bookmarks for PowerToys Command Palette");
        Assert.Contains(results, r => r.LatestVersion == "152.0.7977.65");
    }

    [Fact]
    public void ParseWingetSearchOutput_CrlfEndings_Parses()
    {
        var results = WingetStoreService.ParseWingetSearchOutput(RealSearchOutputCrlf);

        Assert.Equal(2, results.Count);
        Assert.Equal("Google.Chrome", results[0].PackageIdentifier);
        Assert.Equal("Google Chrome", results[0].PackageName);
        Assert.Equal("152.0.7977.65", results[0].LatestVersion);
    }

    [Fact]
    public void ParseWingetSearchOutput_EmptyOutput_ReturnsEmpty()
    {
        Assert.Empty(WingetStoreService.ParseWingetSearchOutput(""));
        Assert.Empty(WingetStoreService.ParseWingetSearchOutput("找不到任何包"));
        Assert.Empty(WingetStoreService.ParseWingetSearchOutput("No package found matching input criteria."));
    }

    [Fact]
    public void GetInstallerUrl_RealShowOutput_FindsHttpUrl()
    {
        // 验证与 GetInstallerUrlViaCliAsync 相同的解析逻辑能处理全角冒号
        var url = ExtractUrlFromShowOutput(RealShowOutput);

        Assert.Equal("https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi", url);
    }

    [Fact]
    public void BuildMirrorCandidates_GitHubReleaseUrl_GeneratesGitCodeVariants()
    {
        // 图吧自身场景：GitHub 仓库 luolangaga/tubatools，GitCode 镜像为 luolangaga/tubatool
        var candidates = WingetStoreService.BuildMirrorCandidates(
            "https://github.com/luolangaga/tubatools/releases/download/v1.5.3/TubaWinUi3_Setup_1.5.3_x64.exe");

        Assert.Equal(3, candidates.Count);
        Assert.Equal(
            "https://gitcode.com/luolangaga/tubatools/releases/download/v1.5.3/TubaWinUi3_Setup_1.5.3_x64.exe",
            candidates[1]);
        Assert.Equal(
            "https://gitcode.com/luolangaga/tubatool/releases/download/v1.5.3/TubaWinUi3_Setup_1.5.3_x64.exe",
            candidates[2]);
    }

    [Fact]
    public void BuildMirrorCandidates_SingularRepo_AppendsTrailingS()
    {
        var candidates = WingetStoreService.BuildMirrorCandidates(
            "https://github.com/example/foo/releases/download/v1/app.exe");

        Assert.Equal("https://gitcode.com/example/foos/releases/download/v1/app.exe", candidates[2]);
    }

    [Fact]
    public void BuildMirrorCandidates_NonGithubUrl_ReturnsOnlyOriginal()
    {
        // 非 GitHub 直链不做镜像探测
        Assert.Single(WingetStoreService.BuildMirrorCandidates(
            "https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi"));
    }

    [Fact]
    public void ParseWingetSearchOutput_OverflowRow_SingleSpaceSeparators_StillParses()
    {
        // 长中文名（图吧工具箱winui3，12 字符/显示宽 18）超过"名称"列宽时，
        // winget 输出退化为单空格分隔、不再补空格对齐 —— 宽空格切分会把整行
        // 合并成一个 token，必须走 ID 正则回退解析。（真实输出样式）
        const string overflowOutput = """
            名称             ID                  版本           匹配
            -----------------------------------------------------------------------------------------------------------------------------
            图吧工具箱winui3 luolangaga.tubatools 1.5.3

            """;

        var results = WingetStoreService.ParseWingetSearchOutput(overflowOutput);

        Assert.Single(results);
        Assert.Equal("luolangaga.tubatools", results[0].PackageIdentifier);
        Assert.Equal("图吧工具箱winui3", results[0].PackageName);
        Assert.Equal("1.5.3", results[0].LatestVersion);
    }

    [Fact]
    public void ParseWingetSearchOutput_OverflowRow_EnglishLongName_Parses()
    {
        // 同场景的英文长名（Teradata Tools and Utilities - Base，超出列宽）
        const string overflowOutput = """
            名称             ID                  版本           匹配
            -----------------------------------------------------------------------------------------------------------------------------
            Teradata Tools and Utilities - Base Teradata.TTUBase 20.00.38.00

            """;

        var results = WingetStoreService.ParseWingetSearchOutput(overflowOutput);

        Assert.Single(results);
        Assert.Equal("Teradata.TTUBase", results[0].PackageIdentifier);
        Assert.Equal("Teradata Tools and Utilities - Base", results[0].PackageName);
        Assert.Equal("20.00.38.00", results[0].LatestVersion);
    }

    [Fact]
    public void ParseWingetSearchOutput_MixedRows_AlignedAndOverflow_BothParsed()
    {
        // 混排：对齐行（2+ 空格）与溢出行（单空格）同时存在
        const string mixedOutput = """
            名称             ID                  版本           匹配
            -----------------------------------------------------------------------------------------------------------------------------
            Google Chrome                                    Google.Chrome                      152.0.7977.65  Moniker: chrome
            图吧工具箱winui3 luolangaga.tubatools 1.5.3

            """;

        var results = WingetStoreService.ParseWingetSearchOutput(mixedOutput);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.PackageIdentifier == "Google.Chrome" && r.LatestVersion == "152.0.7977.65");
        Assert.Contains(results, r => r.PackageIdentifier == "luolangaga.tubatools" && r.PackageName == "图吧工具箱winui3");
    }

    /// <summary>
    /// 复刻 WingetStoreService.GetInstallerUrlViaCliAsync 的 URL 行解析逻辑
    /// </summary>
    private static string? ExtractUrlFromShowOutput(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.Contains("URL", StringComparison.OrdinalIgnoreCase)) continue;
            if (!trimmed.StartsWith("安装程序", StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith("Installer URL", StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith("InstallerUrl", StringComparison.OrdinalIgnoreCase))
                continue;

            var idx = trimmed.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = trimmed.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) return trimmed[idx..].Trim();
        }
        return null;
    }
}