using System.IO.Compression;
using System.Text;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class ErrorReportTests
{
    [Theory]
    [InlineData("故障应用程序名称: TubaWinUi3.exe，版本: 1.6.2.0，时间戳: 0x69a2b1c3")]
    [InlineData("应用程序: TubaWinUI3.BackEnd.exe\nFramework 版本: v10.0.0")]
    [InlineData("The program 图吧工具箱WinUI3_x64.exe version 1.0.0.0 stopped interacting with Windows")]
    [InlineData("Faulting package full name: DA3D64F4.winui3_1.0.1.0_x64__pzq3xp76mxafy")]
    [InlineData("tubawinui3.exe")]
    public void IsAppRelatedEvent_MatchesAppText(string text)
        => Assert.True(ErrorReportService.IsAppRelatedEvent(text));

    [Theory]
    [InlineData("")]
    [InlineData("故障应用程序名称: chrome.exe，版本: 120.0.0.0")]
    [InlineData("Faulting application name: explorer.exe, version: 10.0.26100.1")]
    [InlineData("Faulting application name: TubaPlayer.exe, version: 1.0.0.0")]
    [InlineData("Windows 更新已成功安装")]
    public void IsAppRelatedEvent_RejectsForeignText(string text)
        => Assert.False(ErrorReportService.IsAppRelatedEvent(text));

    [Fact]
    public void BuildEventLogXPath_ContainsAllCrashProviders()
    {
        var xpath = ErrorReportService.BuildEventLogXPath();

        Assert.StartsWith("*[System[Provider[", xpath);
        Assert.Contains("@Name='Application Error'", xpath);
        Assert.Contains("@Name='Application Hang'", xpath);
        Assert.Contains("@Name='.NET Runtime'", xpath);
        Assert.Contains("@Name='Windows Error Reporting'", xpath);
        Assert.EndsWith("]]]", xpath);
    }

    [Fact]
    public void BuildUniqueZipPath_AvoidsOverwriteWithinSameSecond()
    {
        var dir = Directory.CreateTempSubdirectory("tuba-errorreport-");
        try
        {
            var now = new DateTime(2026, 9, 16, 15, 30, 0);

            var first = ErrorReportService.BuildUniqueZipPath(dir.FullName, now);
            Assert.EndsWith("TubaWinUi3-ErrorReport-20260916-153000.zip", first);

            File.WriteAllText(first, "x");
            var second = ErrorReportService.BuildUniqueZipPath(dir.FullName, now);
            Assert.EndsWith("TubaWinUi3-ErrorReport-20260916-153000-2.zip", second);

            File.WriteAllText(second, "x");
            var third = ErrorReportService.BuildUniqueZipPath(dir.FullName, now);
            Assert.EndsWith("TubaWinUi3-ErrorReport-20260916-153000-3.zip", third);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void AddFileWithCap_KeepsSmallFileIntact()
    {
        var dir = Directory.CreateTempSubdirectory("tuba-errorreport-");
        try
        {
            var source = Path.Combine(dir.FullName, "small.log");
            var content = "第一行\r\nsecond line\r\n";
            File.WriteAllText(source, content, new UTF8Encoding(false));

            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                ErrorReportService.AddFileWithCap(zip, "应用日志/small.log", source, 1024);

            Assert.Equal(content, ReadMemoryEntry(buffer, "应用日志/small.log"));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void AddFileWithCap_TruncatesAndKeepsTail()
    {
        var dir = Directory.CreateTempSubdirectory("tuba-errorreport-");
        try
        {
            var source = Path.Combine(dir.FullName, "big.log");
            var tail = new string('b', 256);
            File.WriteAllText(source, new string('a', 4096) + tail, new UTF8Encoding(false));

            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                ErrorReportService.AddFileWithCap(zip, "big.log", source, 512);

            var text = ReadMemoryEntry(buffer, "big.log");
            Assert.Contains("已截断", text);
            Assert.EndsWith(new string('a', 256) + tail, text);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void AddFileWithCap_WritesErrorNoteWhenUnreadable()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"tuba-missing-{Guid.NewGuid():N}.log");

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            ErrorReportService.AddFileWithCap(zip, "missing.log", missing, 1024);

        Assert.Contains("无法读取", ReadMemoryEntry(buffer, "missing.log"));
    }

    [Fact]
    public void WritePackage_IncludesAllSections()
    {
        var dir = Directory.CreateTempSubdirectory("tuba-errorreport-");
        try
        {
            var logSource = Path.Combine(dir.FullName, "agent-debug.log");
            File.WriteAllText(logSource, "[12:00:00.000] INFO hello", new UTF8Encoding(false));

            var zipPath = Path.Combine(dir.FullName, "report.zip");
            var events = new[]
            {
                new ErrorReportService.EventLogItem(
                    new DateTime(2026, 9, 15, 20, 0, 0),
                    "Application Error",
                    1000,
                    "错误",
                    "故障应用程序名称: TubaWinUi3.exe，版本: 1.6.2.0",
                    "<Event><System><EventID>1000</EventID></System></Event>"),
            };
            var logs = new[] { new ErrorReportService.LogFileItem(logSource, "agent-debug.log") };

            ErrorReportService.WritePackage(
                zipPath, events, logs, "异常类型：System.Exception", "OS: Windows 11\r\nCPU: Test",
                null, new DateTime(2026, 9, 16, 15, 30, 0));

            using var zip = ZipFile.OpenRead(zipPath);
            var names = zip.Entries.Select(e => e.FullName).ToList();

            Assert.Contains("说明.txt", names);
            Assert.Contains("系统信息.txt", names);
            Assert.Contains("异常信息.txt", names);
            Assert.Contains("事件日志/事件日志报告.txt", names);
            Assert.Contains("事件日志/原始XML/Event-001-Application Error-1000.xml", names);
            Assert.Contains("应用日志/agent-debug.log", names);

            Assert.Equal("异常类型：System.Exception", ReadZipEntry(zip, "异常信息.txt"));
            Assert.Contains("故障应用程序名称: TubaWinUi3.exe", ReadZipEntry(zip, "事件日志/事件日志报告.txt"));
            Assert.Contains("EventID>1000", ReadZipEntry(zip, "事件日志/原始XML/Event-001-Application Error-1000.xml"));
            Assert.Contains("如何提交", ReadZipEntry(zip, "说明.txt"));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void WritePackage_OmitsErrorSectionWithoutCurrentException()
    {
        var dir = Directory.CreateTempSubdirectory("tuba-errorreport-");
        try
        {
            var zipPath = Path.Combine(dir.FullName, "report.zip");

            ErrorReportService.WritePackage(
                zipPath,
                Array.Empty<ErrorReportService.EventLogItem>(),
                Array.Empty<ErrorReportService.LogFileItem>(),
                null, "OS: Windows 11", null, new DateTime(2026, 9, 16, 15, 30, 0));

            using var zip = ZipFile.OpenRead(zipPath);
            var names = zip.Entries.Select(e => e.FullName).ToList();

            Assert.DoesNotContain("异常信息.txt", names);
            Assert.Contains("未找到与本程序相关的事件日志记录", ReadZipEntry(zip, "事件日志/事件日志报告.txt"));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    private static string ReadMemoryEntry(MemoryStream buffer, string entryName)
    {
        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
        return ReadZipEntry(zip, entryName);
    }

    private static string ReadZipEntry(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd();
    }
}
