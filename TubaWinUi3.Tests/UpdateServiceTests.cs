using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

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
            new() { Name = "TubaWinUi3_x64.zip", BrowserDownloadUrl = "http://a.zip", Size = 100 },
            new() { Name = "TubaWinUi3_x64.exe", BrowserDownloadUrl = "http://a.exe", Size = 100 },
        };
        var result = UpdateService.FindBestAsset(assets);
        Assert.NotNull(result);
        Assert.Equal("TubaWinUi3_x64.exe", result.Name);
    }

    [Fact]
    public void FindBestAsset_ZipWithArch_SecondPriority()
    {
        var assets = new List<UpdateAsset>
        {
            new() { Name = "TubaWinUi3_x64.zip", BrowserDownloadUrl = "http://a.zip", Size = 100 },
            new() { Name = "TubaWinUi3_arm64.zip", BrowserDownloadUrl = "http://b.zip", Size = 100 },
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
            new() { Name = "TubaWinUi3_x64.zip", BrowserDownloadUrl = "http://a.zip", Size = 100 },
            new() { Name = "TubaWinUi3_x64.exe", BrowserDownloadUrl = "http://a.exe", Size = 100 },
        };
        var result = UpdateService.FindBestInstallerAsset(assets);
        Assert.NotNull(result);
        Assert.Equal("TubaWinUi3_x64.exe", result.Name);
    }

    [Fact]
    public void FindBestLiteAsset_ReturnsLiteZipWithArch()
    {
        var assets = new List<UpdateAsset>
        {
            new() { Name = "TubaWinUi3_x64.zip", BrowserDownloadUrl = "http://a.zip", Size = 100 },
            new() { Name = "TubaWinUi3_x64_Lite.zip", BrowserDownloadUrl = "http://b.zip", Size = 100 },
        };
        var result = UpdateService.FindBestLiteAsset(assets);
        Assert.NotNull(result);
        Assert.Equal("TubaWinUi3_x64_Lite.zip", result.Name);
    }

    [Fact]
    public void FindBestPortableAsset_PrefersNonLiteZip()
    {
        var assets = new List<UpdateAsset>
        {
            new() { Name = "TubaWinUi3_x64_Lite.zip", BrowserDownloadUrl = "http://a.zip", Size = 100 },
            new() { Name = "TubaWinUi3_x64.zip", BrowserDownloadUrl = "http://b.zip", Size = 100 },
        };
        var result = UpdateService.FindBestPortableAsset(assets);
        Assert.NotNull(result);
        Assert.Equal("TubaWinUi3_x64.zip", result.Name);
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
    public void IsInstallerFileValid_ExeWithMzHeader_ReturnsTrue()
    {
        var path = Path.Combine(Path.GetTempPath(), "mz_" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x90, 0x00]);
        try
        {
            Assert.True(UpdateService.IsInstallerFileValid(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsInstallerFileValid_NonExeFile_SkipsPeCheck()
    {
        var path = Path.Combine(Path.GetTempPath(), "zip_" + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04]);
        try
        {
            Assert.True(UpdateService.IsInstallerFileValid(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
