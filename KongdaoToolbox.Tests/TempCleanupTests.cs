using KongdaoToolbox.Services;

namespace KongdaoToolbox.Tests;

public class TempCleanupTests
{
    [Theory]
    [InlineData("KongdaoToolbox_Update")]
    [InlineData("KongdaoToolbox_Sandboxie")]
    [InlineData("KongdaoToolbox_Winget")]
    [InlineData("KongdaoToolbox_UniGetUI")]
    [InlineData("KongdaoToolbox_Benchmark")]
    [InlineData("KongdaoToolbox_Extract_12345678-1234-1234-1234-123456789012")]
    [InlineData("KongdaoToolbox_7zip")] // GitHub 便携工具下载暂存（KongdaoToolbox_{toolName}）
    [InlineData("KongdaoCommunity_abc")]
    [InlineData("KongdaoCommunityVerify_7c3b9f1e-a9d5-4a2b-9c0d-1e2f3a4b5c6d")]
    [InlineData("officecli_12345678-1234-1234-1234-123456789012")]
    [InlineData("officecli_12345678-1234-1234-1234-123456789012.html")]
    [InlineData("office_12345678-1234-1234-1234-123456789012.pdf")]
    [InlineData("doceng_12345678-1234-1234-1234-123456789012.pdf")]
    [InlineData("pdfocr_12345678-1234-1234-1234-123456789012")]
    [InlineData("ocr_12345678-1234-1234-1234-123456789012.png")]
    [InlineData("kongdao-ctxprobe-12345678-1234-1234-1234-123456789012.json")]
    [InlineData("kongdao-battery-report.html")]
    [InlineData("kongdao-winui3-setup.exe")] // kongdao- 是各服务的统一临时命名空间，kongdao- 开头即视为本软件产物
    public void IsAppTempEntry_MatchesKnownNames(string name)
    {
        Assert.True(TempCleanupService.IsAppTempEntry(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("7zr.exe")]
    [InlineData("uup-converter-wimlib-10.0.7z")]
    [InlineData("a7zr.exe")]
    [InlineData("KongdaoToolbox")]            // 目录名本身不带下划线时不匹配
    [InlineData("KongdaoCommunityTool.exe")]
    [InlineData(".download")]
    [InlineData("randomfile.tmp")]
    [InlineData("esd_convert_temp_12345678")]
    [InlineData("wget.log")]
    [InlineData("app_crash.log")]
    [InlineData("startup_trace.log")]
    public void IsAppTempEntry_DoesNotMatchForeignNames(string name)
    {
        Assert.False(TempCleanupService.IsAppTempEntry(name));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2.0 KB")]
    [InlineData(1536L * 1024, "1.5 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.00 GB")]
    public void FormatBytes_FormatsSizes(long bytes, string expected)
    {
        Assert.Equal(expected, TempCleanupService.FormatBytes(bytes));
    }
    [Fact]
    public void AutomaticCleanup_PreservesRecentChildrenAndUpdates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var child = Path.Combine(dir, "output.tmp");
            File.WriteAllText(child, "data");
            var cutoff = DateTime.UtcNow.AddDays(-7);
            Directory.SetLastWriteTimeUtc(dir, cutoff.AddDays(-1));
            Assert.False(TempCleanupService.IsExpiredEntry(dir, cutoff));
            File.SetLastWriteTimeUtc(child, cutoff.AddDays(-1));
            Assert.True(TempCleanupService.IsExpiredEntry(dir, cutoff));
            var update = Path.Combine(dir, "KongdaoToolbox_Update");
            Directory.CreateDirectory(update);
            Directory.SetLastWriteTimeUtc(update, cutoff.AddDays(-1));
            Assert.False(TempCleanupService.IsExpiredEntry(update, cutoff));
        }
        finally { Directory.Delete(dir, true); }
    }
}