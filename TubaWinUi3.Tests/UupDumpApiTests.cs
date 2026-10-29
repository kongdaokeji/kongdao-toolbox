using System.Text.Json;
using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// UUP dump JSON API（api.uupdump.net）响应解析测试。
/// </summary>
public class UupDumpApiTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // ==================== 构建列表 ====================

    [Fact]
    public void ParseBuildsJson_ArrayForm_ParsesFields()
    {
        var response = Parse("""
        {
            "apiVersion": "test",
            "builds": [
                { "title": "Windows 11, version 24H2 (26100.9278)", "build": "26100.9278", "arch": "amd64", "created": 1787850077, "uuid": "d922b79f-142d-4cf8-896b-515abfd01e66" },
                { "title": "Windows 11 Insider Preview Feature Update (28120.2824)", "build": "28120.2824", "arch": "arm64", "created": 1788624914, "uuid": "265846e9-900e-418b-b5f8-151016d911e9" }
            ]
        }
        """);

        var builds = UupDumpService.ParseBuildsJson(response);

        Assert.Equal(2, builds.Count);
        Assert.Equal("d922b79f-142d-4cf8-896b-515abfd01e66", builds[0].UpdateId);
        Assert.Equal("26100.9278", builds[0].Build);
        Assert.Equal("amd64", builds[0].Architecture);
        Assert.Equal("正式版", builds[0].Channel);
        Assert.NotNull(builds[0].DateAdded);
        Assert.Equal(2026, builds[0].DateAdded!.Value.Year);
        Assert.Equal("预览体验版", builds[1].Channel);
        Assert.Contains("28120.2824", builds[1].DetailsDisplay);
    }

    [Fact]
    public void ParseBuildsJson_ObjectForm_ParsesSameAsArray()
    {
        // listid.php 带 search 参数时，PHP json_encode 会输出以数字索引为键的对象
        var response = Parse("""
        {
            "apiVersion": "test",
            "builds": {
                "7": { "title": "Windows 11, version 24H2 (26100.9278)", "build": "26100.9278", "arch": "amd64", "created": 1787850077, "uuid": "d922b79f-142d-4cf8-896b-515abfd01e66" },
                "16": { "title": "Windows 11 Insider Preview Canary Channel (28000.2804)", "build": "28000.2804", "arch": "arm64", "created": 1787851825, "uuid": "deee09e7-795b-4ac6-ad80-a8680e97908d" }
            }
        }
        """);

        var builds = UupDumpService.ParseBuildsJson(response);

        Assert.Equal(2, builds.Count);
        Assert.Contains(builds, b => b.UpdateId == "d922b79f-142d-4cf8-896b-515abfd01e66");
        Assert.Contains(builds, b => b.Channel == "预览体验版");
    }

    [Fact]
    public void ParseBuildsJson_EmptyOrMissing_ReturnsEmpty()
    {
        Assert.Empty(UupDumpService.ParseBuildsJson(Parse("""{"apiVersion":"test"}""")));
        Assert.Empty(UupDumpService.ParseBuildsJson(Parse("""{"apiVersion":"test","builds":[]}""")));
    }

    [Theory]
    [InlineData("Windows 11, version 24H2 (26100.9278)", "正式版")]
    [InlineData("Windows 11 Insider Preview Feature Update (28120.2824)", "预览体验版")]
    [InlineData("Windows 11 Insider Preview Canary Channel (28000.2804)", "预览体验版")]
    [InlineData("Windows Server, version 24H2 (26100.9278)", "Server")]
    [InlineData("Microsoft server operating system, version 24H2", "Server")]
    [InlineData("Windows 10, version 22H2 (19045.5011)", "正式版")]
    [InlineData("Preview Update for Windows 11 (28000.2804)", "更新包")]
    [InlineData(".NET Framework Security Update for Windows 11 - KB5120711 (28000.9344) (2)", "更新包")]
    [InlineData("Critical OOBE Update for Windows 11 - KB5122035 (26100.9258)", "更新包")]
    public void DeriveChannel_ClassifiesTitles(string title, string expected)
    {
        Assert.Equal(expected, UupDumpService.DeriveChannel(title));
    }

    // ==================== 语言列表 ====================

    [Fact]
    public void ParseLanguagesJson_UsesZhNamesAndFancyFallback_SortsChineseFirst()
    {
        var response = Parse("""
        {
            "apiVersion": "test",
            "langList": ["en-us", "ja-jp", "zh-cn", "xx-yy", "zh-tw"],
            "langFancyNames": {
                "en-us": "English (United States)",
                "ja-jp": "Japanese",
                "zh-cn": "Chinese (Simplified)",
                "zh-tw": "Chinese (Traditional)",
                "xx-yy": "Unknown Language"
            }
        }
        """);

        var langs = UupDumpService.ParseLanguagesJson(response);

        Assert.Equal(5, langs.Count);
        Assert.Equal("zh-cn", langs[0].Code);
        Assert.Contains("简体中文", langs[0].DisplayName);
        Assert.Equal("zh-tw", langs[1].Code);
        // 中文词典 + API 英文名同时展示
        Assert.Contains("English (United States)", langs.First(l => l.Code == "en-us").DisplayName);
        // 不在词典中的语言回退到 API 英文名
        Assert.Equal("Unknown Language", langs.First(l => l.Code == "xx-yy").DisplayName);
    }

    // ==================== 版本列表 ====================

    [Fact]
    public void ParseEditionsJson_PrefersProfessionalAndUsesZhNames()
    {
        var response = Parse("""
        {
            "apiVersion": "test",
            "editionList": ["CORE", "CORECOUNTRYSPECIFIC", "PROFESSIONAL", "EDUCATION"],
            "editionFancyNames": {
                "CORE": "Windows Home",
                "CORECOUNTRYSPECIFIC": "Windows Home China",
                "PROFESSIONAL": "Windows Pro",
                "EDUCATION": "Windows Education"
            }
        }
        """);

        var editions = UupDumpService.ParseEditionsJson(response);

        Assert.Equal(4, editions.Count);
        Assert.Equal("PROFESSIONAL", editions[0].Id);
        Assert.Equal("Windows 专业版", editions[0].DisplayName);
        Assert.Equal("Windows 家庭版", editions[1].DisplayName);
        Assert.Equal("Windows 家庭中文版", editions[2].DisplayName);
    }

    [Fact]
    public void ParseEditionsJson_UnknownIdFallsBackToFancyName()
    {
        var response = Parse("""
        {
            "editionList": ["SOMEEDITION"],
            "editionFancyNames": { "SOMEEDITION": "Some Edition" }
        }
        """);

        var editions = UupDumpService.ParseEditionsJson(response);

        var ed = Assert.Single(editions);
        Assert.Equal("Some Edition", ed.DisplayName);
    }

    // ==================== 文件清单 ====================

    [Fact]
    public void ParseFilesJson_ParsesFilesObjectAndSumsSizes()
    {
        var response = Parse("""
        {
            "updateName": "Windows 11, version 24H2 (26100.9278)",
            "arch": "amd64",
            "build": "26100.9278",
            "sku": 48,
            "hasUpdates": true,
            "appxPresent": true,
            "files": {
                "DesktopDeployment.cab": { "sha1": "aaa", "size": 13590589, "url": "http://tlu.dl.delivery.mp.microsoft.com/file1", "uuid": "u1", "expire": 0, "debug": null },
                "professional_zh-cn.esd": { "sha1": "bbb", "size": 645922414, "url": "http://tlu.dl.delivery.mp.microsoft.com/file2", "uuid": "u2", "expire": 0, "debug": null },
                "broken.dat": { "sha1": "ccc", "size": 0, "url": "", "uuid": "u3", "expire": 0, "debug": null }
            }
        }
        """);

        var set = UupDumpService.ParseFilesJson(response);

        Assert.Equal("Windows 11, version 24H2 (26100.9278)", set.UpdateName);
        Assert.Equal("amd64", set.Architecture);
        // 大小为 0 且无直链的异常条目被过滤
        Assert.Equal(2, set.Files.Count);
        Assert.All(set.Files, f => Assert.StartsWith("http://tlu.dl.delivery.mp.microsoft.com/", f.Url));
        Assert.Equal(13590589L + 645922414L, set.TotalSize);
    }

    // ==================== 转换配置 ====================

    [Fact]
    public void BuildConvertConfigIni_DefaultMatchesOfficial()
    {
        var ini = UupDumpService.BuildConvertConfigIni();

        Assert.Contains("[convert-UUP]", ini);
        Assert.Contains("AutoStart    =1", ini);
        Assert.Contains("AddUpdates   =1", ini);
        Assert.Contains("Cleanup      =0", ini);
        Assert.Contains("NetFx3       =0", ini);
        Assert.Contains("StartVirtual =0", ini);
        Assert.Contains("wim2esd      =0", ini);
        Assert.Contains("SkipISO      =0", ini);
        Assert.Contains("SkipApps     =1", ini);
        Assert.Contains("[create_virtual_editions]", ini);
        Assert.Contains("vAutoEditions=", ini);
    }

    [Fact]
    public void BuildConvertConfigIni_FullOptionsMatchesOfficialMapping()
    {
        // 与官网 download.php 的 updates/cleanup/netfx/esd/virtualEditions 参数一一对应
        var ini = UupDumpService.BuildConvertConfigIni(new UupConvertOptions
        {
            AddUpdates = false,
            Cleanup = true,
            NetFx3 = true,
            Wim2Esd = true,
            SkipApps = false,
            VirtualEditions = ["Enterprise", "Education"],
        });

        Assert.Contains("AddUpdates   =0", ini);
        Assert.Contains("Cleanup      =1", ini);
        Assert.Contains("NetFx3       =1", ini);
        Assert.Contains("wim2esd      =1", ini);
        Assert.Contains("vwim2esd     =1", ini);   // 官网勾选 ESD 压缩时两处同时置 1
        Assert.Contains("StartVirtual =1", ini);
        Assert.Contains("SkipApps     =0", ini);
        Assert.Contains("vAutoEditions=Enterprise,Education", ini);
    }

    // ==================== 附加版本（虚拟版本） ====================

    [Fact]
    public void GetVirtualEditionsForBase_ProfessionalMatchesWebsite()
    {
        // 与官网 download.php 对 Windows Pro 基础版列出的附加版本一致
        var ves = UupDumpService.GetVirtualEditionsForBase("PROFESSIONAL");

        var names = ves.Select(v => v.Name).ToList();
        Assert.Equal(
            ["ProfessionalWorkstation", "ProfessionalEducation", "Education", "Enterprise", "ServerRdsh", "IoTEnterprise", "IoTEnterpriseK"],
            names);
    }

    [Fact]
    public void GetVirtualEditionsForBase_CoreHasSingleLanguage()
    {
        var ves = UupDumpService.GetVirtualEditionsForBase("core");

        var ve = Assert.Single(ves);
        Assert.Equal("CoreSingleLanguage", ve.Name);
    }

    [Fact]
    public void GetVirtualEditionsForBase_UnknownBaseReturnsEmpty()
    {
        Assert.Empty(UupDumpService.GetVirtualEditionsForBase("CORECOUNTRYSPECIFIC"));
        Assert.Empty(UupDumpService.GetVirtualEditionsForBase("NOT_A_EDITION"));
    }

    // ==================== 错误信息 ====================

    [Theory]
    [InlineData("USER_RATE_LIMITED", "限制")]
    [InlineData("NO_UPDATE_FOUND", "未在 Windows Update 服务器找到")]
    [InlineData("UNKNOWN_CODE_XYZ", "UNKNOWN_CODE_XYZ")]
    public void GetFriendlyErrorMessage_MapsCodes(string code, string expectedFragment)
    {
        Assert.Contains(expectedFragment, UupDumpService.GetFriendlyErrorMessage(code));
    }

    // ==================== 转换工具清单 ====================

    [Fact]
    public void ParseConverterManifest_ParsesOfficialAria2Format()
    {
        // 与 git.uupdump.net/uup-dump/misc autodl_files/converter_windows 实际内容一致
        var manifest = """
        https://uupdump.net/misc/7zr.exe
          out=7zr.exe
          checksum=sha-256=72c98287b2e8f85ea7bb87834b6ce1ce7ce7f41a8c97a81b307d4d4bf900922b

        https://uupdump.net/misc/uup-converter-wimlib-v125r.7z
          out=uup-converter-wimlib.7z
          checksum=sha-256=bc2e7a45c6e8d3304da487d21d4e33c21d20cc39ae4725b4a378683e18356165
        """;

        var files = UupDumpService.ParseConverterManifest(manifest);

        Assert.Equal(2, files.Count);
        Assert.Equal("https://uupdump.net/misc/7zr.exe", files[0].Url);
        Assert.Equal("7zr.exe", files[0].FileName);
        Assert.Equal("72c98287b2e8f85ea7bb87834b6ce1ce7ce7f41a8c97a81b307d4d4bf900922b", files[0].Sha256);
        // 远端带版本号，本地名取 out=（不带版本）
        Assert.Equal("https://uupdump.net/misc/uup-converter-wimlib-v125r.7z", files[1].Url);
        Assert.Equal("uup-converter-wimlib.7z", files[1].FileName);
        Assert.Equal("bc2e7a45c6e8d3304da487d21d4e33c21d20cc39ae4725b4a378683e18356165", files[1].Sha256);
    }

    [Fact]
    public void ParseConverterManifest_IncompleteEntriesSkipped()
    {
        var manifest = """
        https://uupdump.net/misc/only-url.exe

        garbage line
          checksum=sha-256=abc
        """;

        Assert.Empty(UupDumpService.ParseConverterManifest(manifest));
    }

    // ==================== 目录与校验 ====================

    [Fact]
    public void GetPackageDirs_BuildsSanitizedLayout()
    {
        var (packageDir, uupsDir) = UupDumpService.GetPackageDirs("26100.9278", "zh-cn", "professional");

        Assert.EndsWith("26100.9278_zh-cn_professional", packageDir);
        Assert.Equal(Path.Combine(packageDir, "UUPs"), uupsDir);
    }

    [Fact]
    public async Task VerifySha256Async_MatchesKnownHash()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"uup_sha_test_{Guid.NewGuid():N}.bin");
        try
        {
            // sha256("tuba") = 未知，改为用 SHA256.Create 现场计算
            var data = new byte[] { 1, 2, 3, 4, 5 };
            await File.WriteAllBytesAsync(temp, data);
            using var sha = System.Security.Cryptography.SHA256.Create();
            var expected = Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();

            Assert.True(await UupDumpService.VerifySha256Async(temp, expected, CancellationToken.None));
            Assert.False(await UupDumpService.VerifySha256Async(temp, new string('0', 64), CancellationToken.None));
            Assert.False(await UupDumpService.VerifySha256Async(temp + ".missing", expected, CancellationToken.None));
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
