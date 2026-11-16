using System.Text.Json;
using KongdaoToolbox.Models;
using KongdaoToolbox.Services;

namespace KongdaoToolbox.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(500L, "500 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1048576L, "1.0 MB")]
    [InlineData(1073741824L, "1.00 GB")]
    [InlineData(1610612736L, "1.50 GB")]
    [InlineData(536870912L, "512.0 MB")]
    public void FormatSize_FormatsCorrectly(long bytes, string expected)
    {
        Assert.Equal(expected, UpdateService.FormatSize(bytes));
    }

    [Theory]
    [InlineData(0.5, "500 Kbps")]
    [InlineData(1.0, "1.00 Mbps")]
    [InlineData(100.0, "100.00 Mbps")]
    [InlineData(1000.0, "1.00 Gbps")]
    [InlineData(2500.0, "2.50 Gbps")]
    public void FormatSpeed_FormatsCorrectly(double mbps, string expected)
    {
        Assert.Equal(expected, UpdateService.FormatSpeed(mbps));
    }

    [Fact]
    public void FormatTime_NullTime_ReturnsDashes()
    {
        Assert.Equal("--", UpdateService.FormatTime(null));
    }

    [Fact]
    public void FormatTime_ZeroSeconds_ReturnsDashes()
    {
        Assert.Equal("--", UpdateService.FormatTime(TimeSpan.Zero));
    }

    [Fact]
    public void FormatTime_NegativeTime_ReturnsDashes()
    {
        Assert.Equal("--", UpdateService.FormatTime(TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void FormatTime_SecondsOnly()
    {
        Assert.Equal("30s", UpdateService.FormatTime(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void FormatTime_MinutesAndSeconds()
    {
        Assert.Equal("5m 30s", UpdateService.FormatTime(TimeSpan.FromSeconds(330)));
    }

    [Fact]
    public void FormatTime_HoursAndMinutes()
    {
        Assert.Equal("2h 30m", UpdateService.FormatTime(TimeSpan.FromMinutes(150)));
    }

    [Fact]
    public void FindBestAsset_ExeWithArch_FirstPriority()
    {
        var assets = new List<UpdateAsset>
        {
            new() { Name = "KongdaoToolbox_x64.zip", BrowserDownloadUrl = "http://a.zip", Size = 100 },
            new() { Name = "KongdaoToolbox_x64.exe", BrowserDownloadUrl = "http://a.exe", Size = 100 },
        };
        var result = UpdateService.FindBestAsset(assets);
        Assert.NotNull(result);
        Assert.Equal("KongdaoToolbox_x64.exe", result.Name);
    }

    [Fact]
    public void FindBestAsset_ZipWithArch_SecondPriority()
    {
        var assets = new List<UpdateAsset>
        {
            new() { Name = "KongdaoToolbox_x64.zip", BrowserDownloadUrl = "http://a.zip", Size = 100 },
            new() { Name = "KongdaoToolbox_arm64.zip", BrowserDownloadUrl = "http://b.zip", Size = 100 },
        };
        var result = UpdateService.FindBestAsset(assets);
        Assert.NotNull(result);
        Assert.Contains(UpdateService.CurrentArchitecture, result.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindBestAsset_NoArchMatch_ReturnsNullIfNoMatch()
    {
        var assets = new List<UpdateAsset>
        {
            new() { Name = "SomeOtherFile.txt", BrowserDownloadUrl = "http://a.txt", Size = 100 },
        };
        var result = UpdateService.FindBestAsset(assets);
        Assert.Null(result);
    }

    [Fact]
    public void FindBestAsset_EmptyList_ReturnsNull()
    {
        Assert.Null(UpdateService.FindBestAsset([]));
    }

    [Fact]
    public void FindBestInstallerAsset_ReturnsExeWithArch()
    {
        var assets = new List<UpdateAsset>
        {
            new() { Name = "KongdaoToolbox_x64.zip", BrowserDownloadUrl = "http://a.zip", Size = 100 },
            new() { Name = "KongdaoToolbox_x64.exe", BrowserDownloadUrl = "http://a.exe", Size = 100 },
        };
        var result = UpdateService.FindBestInstallerAsset(assets);
        Assert.NotNull(result);
        Assert.Equal("KongdaoToolbox_x64.exe", result.Name);
    }

    [Fact]
    public void FindBestLiteAsset_ReturnsLiteZipWithArch()
    {
        var assets = new List<UpdateAsset>
        {
            new() { Name = "KongdaoToolbox_x64.zip", BrowserDownloadUrl = "http://a.zip", Size = 100 },
            new() { Name = "KongdaoToolbox_x64_Lite.zip", BrowserDownloadUrl = "http://b.zip", Size = 100 },
        };
        var result = UpdateService.FindBestLiteAsset(assets);
        Assert.NotNull(result);
        Assert.Equal("KongdaoToolbox_x64_Lite.zip", result.Name);
    }

    [Fact]
    public void FindBestPortableAsset_PrefersNonLiteZip()
    {
        var assets = new List<UpdateAsset>
        {
            new() { Name = "KongdaoToolbox_x64_Lite.zip", BrowserDownloadUrl = "http://a.zip", Size = 100 },
            new() { Name = "KongdaoToolbox_x64.zip", BrowserDownloadUrl = "http://b.zip", Size = 100 },
        };
        var result = UpdateService.FindBestPortableAsset(assets);
        Assert.NotNull(result);
        Assert.Equal("KongdaoToolbox_x64.zip", result.Name);
    }

    [Fact]
    public void FindBestPortableAsset_MatchesReleaseConventionWithoutPortableSuffix()
    {
        // 发版约定：<产品名>_<版本>_<架构>.zip。匹配只要求「含架构串 + .zip + 非 Lite」，
        // 不要求 "Portable" 字样 —— 这条锁死约定，防止以后又把 "_Portable" 加回去。
        var arch = UpdateService.CurrentArchitecture;
        var name = $"KongdaoToolbox_1.9.1_{arch}.zip";
        var assets = new List<UpdateAsset>
        {
            new() { Name = name, BrowserDownloadUrl = "http://a.zip", Size = 100 },
        };

        var result = UpdateService.FindBestPortableAsset(assets);

        Assert.NotNull(result);
        Assert.Equal(name, result.Name);
    }

    [Fact]
    public void FindBestPortableAsset_OldPortableNamingWithoutArch_ReturnsNull()
    {
        // 历史踩坑回归：曾经发过 KongdaoToolbox_v1.9.0_portable.zip（小写、无架构串），
        // 三级匹配全部落空 → 用户点「开始更新」拿不到任何包。
        var assets = new List<UpdateAsset>
        {
            new() { Name = "KongdaoToolbox_v1.9.0_portable.zip", BrowserDownloadUrl = "http://a.zip", Size = 100 },
        };

        Assert.Null(UpdateService.FindBestPortableAsset(assets));
    }

    [Fact]
    public void FindBestInstallerAsset_ZipOnlyRelease_ReturnsNull()
    {
        // 关键回归：交付包一旦带 .installed 标记，RuntimeHelper.IsInstalled 为 true，
        // AutoDownloadUpdate 会改走 FindBestInstallerAsset —— 它硬性要求 .exe。
        // 我们只发 zip，于是必然返回 null、自动更新彻底失效。
        // 这就是「便携包绝不能带 .installed」的原因。
        var arch = UpdateService.CurrentArchitecture;
        var assets = new List<UpdateAsset>
        {
            new() { Name = $"KongdaoToolbox_1.9.1_{arch}.zip", BrowserDownloadUrl = "http://a.zip", Size = 100 },
        };

        Assert.Null(UpdateService.FindBestInstallerAsset(assets));
        // 同样的资产在便携分支下是能拿到的 —— 两个分支的差异就是 .installed 一个文件
        Assert.NotNull(UpdateService.FindBestPortableAsset(assets));
    }

    [Fact]
    public void CurrentArchitecture_IsValidArchString()
    {
        Assert.Contains(UpdateService.CurrentArchitecture, new[] { "x64", "x86", "arm64" });
    }

    [Fact]
    public void IsInstallerFileValid_MissingFile_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), "missing_" + Guid.NewGuid().ToString("N") + ".exe");
        Assert.False(UpdateService.IsInstallerFileValid(path));
    }

    [Fact]
    public void IsInstallerFileValid_EmptyFile_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), "empty_" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(path, []);
        try
        {
            Assert.False(UpdateService.IsInstallerFileValid(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsInstallerFileValid_ExeWithoutMzHeader_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), "noMZ_" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);
        try
        {
            Assert.False(UpdateService.IsInstallerFileValid(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsInstallerFileValid_ExeWithMzHeaderWithoutPublisherProof_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), "mz_" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x90, 0x00]);
        try
        {
            Assert.False(UpdateService.IsInstallerFileValid(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsInstallerFileValid_NonExeFileWithoutPublisherProof_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), "zip_" + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04]);
        try
        {
            Assert.False(UpdateService.IsInstallerFileValid(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseUpdateJson_PrereleaseNewerThanCurrent_ReturnsNull()
    {
        // 更新通道只跟随正式版：预览版（prerelease）即使版本号更高也不作为更新
        var json = """
            {
              "tag_name": "v9999.0.0",
              "prerelease": true,
              "published_at": "2026-09-01T00:00:00Z",
              "assets": [
                {
                  "name": "KongdaoToolbox_Setup_9999.0.0_x64.exe",
                  "browser_download_url": "https://example.com/setup.exe",
                  "size": 1,
                  "type": "installer"
                }
              ]
            }
            """;

        Assert.Null(UpdateService.ParseUpdateJson(json));
    }

    [Fact]
    public void ParseUpdateJson_StableNewerThanCurrent_ReturnsUpdate()
    {
        var json = """
            {
              "tag_name": "v9999.0.0",
              "prerelease": false,
              "published_at": "2026-09-01T00:00:00Z",
              "assets": [
                {
                  "name": "KongdaoToolbox_Setup_9999.0.0_x64.exe",
                  "browser_download_url": "https://example.com/setup.exe",
                  "size": 1,
                  "type": "installer"
                }
              ]
            }
            """;

        var update = UpdateService.ParseUpdateJson(json);

        Assert.NotNull(update);
        Assert.Equal("9999.0.0", update!.Version);
    }

    [Fact]
    public void ParseUpdateJson_UpstreamRelease_ReturnsNull()
    {
        // 更新源一旦误指到上游（或仓库被重定向到上游），绝不能提示用户去装上游原版：
        // 那会把空岛品牌、图标、文案整体覆盖掉。
        var json = """
            {
              "tag_name": "v9999.0.0",
              "name": "图吧工具箱CE v9999.0.0",
              "html_url": "https://github.com/luolangaga/tubatools/releases/tag/v9999.0.0",
              "prerelease": false,
              "published_at": "2026-09-01T00:00:00Z",
              "assets": [
                {
                  "name": "TubaWinUi3_Portable_9999.0.0_x64.zip",
                  "browser_download_url": "https://github.com/luolangaga/tubatools/releases/download/v9999.0.0/TubaWinUi3_Portable_9999.0.0_x64.zip",
                  "size": 1,
                  "type": "installer"
                }
              ]
            }
            """;

        Assert.Null(UpdateService.ParseUpdateJson(json));
    }

    [Fact]
    public void ParseUpdateJson_UpstreamOwnerOnlyInAssetUrl_ReturnsNull()
    {
        // 标题与标签都干净，但资产下载地址落在上游账号下 —— 同样判为不可信。
        // 这条路径对应「GitCode 镜像配错 / 仓库 fork 后没改下载地址」。
        var json = """
            {
              "tag_name": "v9999.0.0",
              "name": "Kongdao Toolbox v9999.0.0",
              "html_url": "https://github.com/kongdaokeji/kongdao-toolbox/releases/tag/v9999.0.0",
              "prerelease": false,
              "published_at": "2026-09-01T00:00:00Z",
              "assets": [
                {
                  "name": "KongdaoToolbox_9999.0.0_x64.zip",
                  "browser_download_url": "https://raw.githubusercontent.com/luolangaga/tubatools/master/evil.zip",
                  "size": 1,
                  "type": "installer"
                }
              ]
            }
            """;

        Assert.Null(UpdateService.ParseUpdateJson(json));
    }

    [Fact]
    public void ParseUpdateJson_KongdaoPortableRelease_ReturnsUpdateAndPicksAsset()
    {
        // 自家发版命名约定：<产品名>_<版本>_<架构>.zip（不要 _Portable 字样）
        // （与上游资产命名格式兼容，从而复用了 FindBestPortableAsset 的架构校验）
        var json = """
            {
              "tag_name": "v9999.0.0",
              "name": "空岛图吧工具箱 v9999.0.0",
              "html_url": "https://github.com/kongdaokeji/kongdao-toolbox/releases/tag/v9999.0.0",
              "prerelease": false,
              "published_at": "2026-09-01T00:00:00Z",
              "assets": [
                {
                  "name": "KongdaoToolbox_9999.0.0_x64.zip",
                  "browser_download_url": "https://github.com/kongdaokeji/kongdao-toolbox/releases/download/v9999.0.0/KongdaoToolbox_9999.0.0_x64.zip",
                  "size": 1,
                  "type": "installer"
                }
              ]
            }
            """;

        var update = UpdateService.ParseUpdateJson(json);

        Assert.NotNull(update);
        var asset = UpdateService.FindBestPortableAsset(update!.Assets);
        Assert.NotNull(asset);
        Assert.Equal("KongdaoToolbox_9999.0.0_x64.zip", asset!.Name);
    }

    [Fact]
    public void ParseUpdateJson_KongdaoToolsInstallerAsset_IsNotTreatedAsUpstream()
    {
        // 回归护栏：我们的安装包叫 KongdaoTools_Setup_<ver>_x64.exe，含 "KongdaoTools"
        // 子串。品牌校验若用裸 "kongdaotools" 做标记，会把自家安装包判成上游，直接断掉
        // 已安装用户的更新通道。
        var json = """
            {
              "tag_name": "v9999.0.0",
              "name": "空岛图吧工具箱 v9999.0.0",
              "html_url": "https://github.com/kongdaokeji/kongdao-toolbox/releases/tag/v9999.0.0",
              "prerelease": false,
              "published_at": "2026-09-01T00:00:00Z",
              "assets": [
                {
                  "name": "KongdaoTools_Setup_9999.0.0_x64.exe",
                  "browser_download_url": "https://github.com/kongdaokeji/kongdao-toolbox/releases/download/v9999.0.0/KongdaoTools_Setup_9999.0.0_x64.exe",
                  "size": 1,
                  "type": "installer"
                }
              ]
            }
            """;

        var update = UpdateService.ParseUpdateJson(json);

        Assert.NotNull(update);
        var asset = UpdateService.FindBestInstallerAsset(update!.Assets);
        Assert.NotNull(asset);
        Assert.Equal("KongdaoTools_Setup_9999.0.0_x64.exe", asset!.Name);
    }

    [Fact]
    public void PickNewestStableReleaseJson_SkipsNewerPrereleaseAndDraft()
    {
        // GitHub 列表最新在前；更新的预发布与草稿都不参与
        var json = """
            [
              { "tag_name": "v9.9.9", "prerelease": true, "draft": false, "published_at": "2026-09-10T00:00:00Z" },
              { "tag_name": "v9.9.8", "prerelease": false, "draft": true, "published_at": "2026-09-09T00:00:00Z" },
              { "tag_name": "v2.4.0", "prerelease": false, "draft": false, "published_at": "2026-09-01T00:00:00Z" }
            ]
            """;

        var picked = UpdateService.PickNewestStableReleaseJson(json);

        Assert.NotNull(picked);
        Assert.Equal("v2.4.0", JsonDocument.Parse(picked!).RootElement.GetProperty("tag_name").GetString());
    }

    [Fact]
    public void PickNewestStableReleaseJson_GitCodeOrderOldestFirst_ReturnsNewest()
    {
        // GitCode 列表最旧在前，不能依赖数组顺序
        var json = """
            [
              { "tag_name": "v2.3.0", "prerelease": false, "draft": false, "created_at": "2026-08-01T00:00:00+08:00" },
              { "tag_name": "v2.5.0", "prerelease": false, "draft": false, "created_at": "2026-09-01T00:00:00+08:00" },
              { "tag_name": "v2.4.0", "prerelease": false, "draft": false, "created_at": "2026-08-15T00:00:00+08:00" }
            ]
            """;

        var picked = UpdateService.PickNewestStableReleaseJson(json);

        Assert.NotNull(picked);
        Assert.Equal("v2.5.0", JsonDocument.Parse(picked!).RootElement.GetProperty("tag_name").GetString());
    }

    [Fact]
    public void PickNewestStableReleaseJson_OnlyPrereleases_ReturnsNull()
    {
        var json = """
            [
              { "tag_name": "v2.6.0-beta1", "prerelease": true, "draft": false, "published_at": "2026-09-10T00:00:00Z" },
              { "tag_name": "v2.5.0-rc1", "prerelease": true, "draft": false, "published_at": "2026-09-05T00:00:00Z" }
            ]
            """;

        Assert.Null(UpdateService.PickNewestStableReleaseJson(json));
    }

    [Fact]
    public void PickNewestStableReleaseJson_InvalidJson_ReturnsNull()
    {
        Assert.Null(UpdateService.PickNewestStableReleaseJson("not json"));
        Assert.Null(UpdateService.PickNewestStableReleaseJson("{}"));
    }
}
