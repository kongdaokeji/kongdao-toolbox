using System.Runtime.InteropServices;
using System.Text;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class StartupManagerToolTests
{
    [Fact]
    public void Parse_SkipsBannerAndContainerRows_MapsColumns()
    {
        const string csv = """
            Sysinternals Autoruns v14.2 - Autostart program viewer
            Copyright (C) 2002-2026 Mark Russinovich
            Sysinternals - www.sysinternals.com

            Time,Entry Location,Entry,Enabled,Category,Profile,Description,Signer,Company,Image Path,Version,Launch String
            2024/4/1 15:26,HKLM\System\CurrentControlSet\Control\Terminal Server\Wds\rdpwd\StartupPrograms,,,"Logon",System-wide,,,,,,
            1976/9/29 12:09,"HKLM\System\CurrentControlSet\Control\Terminal Server\Wds\rdpwd\StartupPrograms","rdpclip",enabled,"Logon",System-wide,"剪贴板监视程序","(Verified) Microsoft Windows","Microsoft Corporation","c:\windows\system32\rdpclip.exe",10.0.26100.8972,"rdpclip"
            """;

        var entries = StartupCsvParser.Parse(csv);

        // 容器行（只有位置、没有具体启动项）应被跳过
        Assert.Single(entries);
        var e = entries[0];
        Assert.Equal("rdpclip", e.Entry);
        Assert.Equal(@"HKLM\System\CurrentControlSet\Control\Terminal Server\Wds\rdpwd\StartupPrograms", e.EntryLocation);
        Assert.True(e.IsEnabled);
        Assert.Equal("Logon", e.Category);
        Assert.Equal("登录启动", e.CategoryDisplay);
        Assert.Equal("剪贴板监视程序", e.Description);
        Assert.Equal("Verified", e.Signing.Key);
        Assert.Equal("Microsoft Windows", e.Signing.Name);
        Assert.Equal("Microsoft Corporation", e.Company);
        Assert.Equal(@"c:\windows\system32\rdpclip.exe", e.ImagePath);
        Assert.Equal("10.0.26100.8972", e.Version);
        Assert.Equal("rdpclip", e.LaunchString);
        Assert.False(e.FileMissing);
        Assert.True(e.HasImage);
    }

    [Fact]
    public void Parse_HandlesQuotesCommasAndEscapedQuotesInFields()
    {
        // Launch String 里同时有引号、转义引号和逗号（真实 v14 输出样式）
        var csv =
            "Time,Entry Location,Entry,Enabled,Category,Profile,Description,Signer,Company,Image Path,Version,Launch String\r\n" +
            "2025/12/15 14:07,\"HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run\",\"QuarkUpdaterTaskUser1.0.0.21\",disabled,\"Logon\",LAPTOP-CE9T4R0L\\luolan,\"QuarkUpdater (x64)\",\"(Verified) ALIBABA (CHINA) NETWORK TECHNOLOGY CO.,LTD.\",\"The Chromium Authors\",\"c:\\users\\luolan\\appdata\\local\\quarkupdater\\updater.exe\",1.0.0.21," +
            "\"\"\"C:\\Users\\luolan\\AppData\\Local\\QuarkUpdater\\updater.exe\"\" --wake --enable-logging --vmodule=*/components/winhttp/*=1,*/components/update_client/*=2,*/chrome/updater/*=2\"\r\n";

        var entries = StartupCsvParser.Parse(csv);

        Assert.Single(entries);
        var e = entries[0];
        Assert.False(e.IsEnabled);
        // 含逗号的签名者字段（在引号内）应完整保留
        Assert.Equal("(Verified) ALIBABA (CHINA) NETWORK TECHNOLOGY CO.,LTD.", e.Signer);
        Assert.Equal("Verified", e.Signing.Key);
        // 启动参数：外层引号去掉、内层转义引号还原
        Assert.Equal(
            "\"C:\\Users\\luolan\\AppData\\Local\\QuarkUpdater\\updater.exe\" --wake --enable-logging --vmodule=*/components/winhttp/*=1,*/components/update_client/*=2,*/chrome/updater/*=2",
            e.LaunchString);
    }

    [Theory]
    [InlineData("a,b,\"c,d\",\"e\"\"f\",\"\",", new[] { "a", "b", "c,d", "e\"f", "", "" })]
    [InlineData("plain", new[] { "plain" })]
    [InlineData("", new[] { "" })]
    public void ParseCsvLine_SplitsRfc4180(string line, string[] expected)
    {
        Assert.Equal(expected, StartupCsvParser.ParseCsvLine(line));
    }

    [Theory]
    [InlineData("", "None", "")]
    [InlineData("(Verified) Microsoft Windows", "Verified", "Microsoft Windows")]
    [InlineData("(Not verified) Some Co.", "NotVerified", "Some Co.")]
    [InlineData("(Expired) Old Corp", "Expired", "Old Corp")]
    [InlineData("(Not trusted) Evil Ltd", "NotTrusted", "Evil Ltd")]
    [InlineData("plain text", "Unknown", "plain text")]
    public void ParseSigner_ClassifiesStatus(string signer, string key, string name)
    {
        var info = StartupCsvParser.ParseSigner(signer);
        Assert.Equal(key, info.Key);
        Assert.Equal(name, info.Name);
    }

    [Theory]
    [InlineData("Logon", "登录启动")]
    [InlineData("Scheduled Tasks", "计划任务")]
    [InlineData("Services", "服务")]
    [InlineData("Boot Execute", "启动执行")]
    [InlineData("Winlogon", "Winlogon 通知")]
    [InlineData("Image Hijacks", "映像劫持")]
    [InlineData("Some New Category", "Some New Category")]
    public void GetCategoryDisplay_MapsKnownValues(string category, string expected)
    {
        Assert.Equal(expected, StartupCsvParser.GetCategoryDisplay(category));
    }

    [Theory]
    [InlineData("enabled", true)]
    [InlineData("disabled", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    public void Entry_EnabledParsing(string raw, bool expected)
    {
        var e = new AutorunsEntry { EnabledRaw = raw };
        Assert.Equal(expected, e.IsEnabled);
    }

    [Fact]
    public void Entry_DetectsMissingFile()
    {
        var e = new AutorunsEntry { ImagePath = "File not found: C:\\Program Files\\X\\x.exe" };
        Assert.True(e.FileMissing);
        Assert.Equal(@"C:\Program Files\X\x.exe", e.MissingPath);
        Assert.False(e.HasImage);
        Assert.Equal("None", e.Signing.Key);   // 空签名者 → 未签名（属于风险项）

        var ok = new AutorunsEntry { ImagePath = "\"C:\\Program Files\\X\\x.exe\"" };
        Assert.False(ok.FileMissing);
        Assert.True(ok.HasImage);
        Assert.Equal(@"C:\Program Files\X\x.exe", ok.OpenablePath);
    }

    [Fact]
    public void DecodeOutput_HandlesUtf16LeWithBom()
    {
        var text = "Time,Entry Location\n\"剪贴板\",x";
        var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(text)).ToArray();
        Assert.Equal(text, StartupManagerTool.DecodeOutput(bytes));
    }

    [Fact]
    public void DecodeOutput_HandlesUtf8WithBom()
    {
        var text = "Time,Entry\n\"路径\",C:\\x";
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        Assert.Equal(text, StartupManagerTool.DecodeOutput(bytes));
    }

    [Fact]
    public void DecodeOutput_StrictUtf8PlainText()
    {
        var bytes = Encoding.UTF8.GetBytes("plain ascii");
        Assert.Equal("plain ascii", StartupManagerTool.DecodeOutput(bytes));
    }

    [Fact]
    public void PickAutorunscExe_PrefersProcessArchitecture()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "autorunsc.exe"), "");
            File.WriteAllText(Path.Combine(dir, "autorunsc64.exe"), "");
            File.WriteAllText(Path.Combine(dir, "autorunsc64a.exe"), "");

            var expected = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "autorunsc64a.exe",
                Architecture.X86 => "autorunsc.exe",
                _ => "autorunsc64.exe"
            };
            Assert.Equal(expected, Path.GetFileName(StartupManagerTool.PickAutorunscExe(dir)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void PickAutorunscExe_MissingDirectory_ReturnsNull()
    {
        Assert.Null(StartupManagerTool.PickAutorunscExe(Path.Combine(Path.GetTempPath(), "不存在_" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public async Task GetIconPathAsync_ExtractsRealPng()
    {
        // 用测试宿主自身的 exe（含图标资源）验证整条提取链路
        var png = await StartupIconService.GetIconPathAsync(Environment.ProcessPath!);
        Assert.NotNull(png);
        Assert.True(File.Exists(png));
        Assert.True(new FileInfo(png!).Length > 0);

        var bytes = File.ReadAllBytes(png!);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]); // PNG 魔数
    }

    [Fact]
    public async Task GetIconPathAsync_MissingFile_ReturnsNull()
    {
        Assert.Null(await StartupIconService.GetIconPathAsync(Path.Combine(Path.GetTempPath(), "不存在_" + Guid.NewGuid().ToString("N") + ".exe")));
    }

    // ---- 操作（禁用/恢复）相关 ----

    [Theory]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "registry")]
    [InlineData(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "registry")]
    [InlineData(@"HKLM\System\CurrentControlSet\Services\AarSvc", "registry")]
    [InlineData(@"HKLM\System\CurrentControlSet\Services", "service")]
    [InlineData("Task Scheduler", "task")]
    [InlineData(@"C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup", "file")]
    [InlineData(@"", null)]
    public void DetectKind_ClassifiesEntries(string location, string? expected)
    {
        var e = new AutorunsEntry { EntryLocation = location, Entry = location.Length > 0 ? "X" : "" };
        Assert.Equal(expected, StartupActionService.DetectKind(e));
    }

    [Theory]
    [InlineData(@"HKLM\SOFTWARE\X", "HKLM", @"SOFTWARE\X")]
    [InlineData(@"HKCU\Software\Microsoft", "HKCU", @"Software\Microsoft")]
    public void ParseRegistryLocation_SplitsHiveAndPath(string location, string hive, string sub)
    {
        var (h, s) = StartupActionService.ParseRegistryLocation(location);
        Assert.Equal(hive, h);
        Assert.Equal(sub, s);
    }

    [Fact]
    public void ParseRegistryLocation_RejectsUnknownHive()
    {
        Assert.ThrowsAny<Exception>(() => StartupActionService.ParseRegistryLocation(@"FOO\Bar"));
    }

    [Theory]
    [InlineData("        START_TYPE               : 2   AUTO_START", "auto")]
    [InlineData("          START_TYPE         : 4   DISABLED", "disabled")]
    [InlineData("START_TYPE : 3 DEMAND_START", "demand")]
    [InlineData("no start type here", null)]
    [InlineData("", null)]
    public void ParseServiceStartType_ExtractsKeyword(string output, string? expected)
    {
        Assert.Equal(expected, StartupActionService.ParseServiceStartType(output));
    }

    [Fact]
    public void KeyOf_JoinsLocationAndEntry()
    {
        Assert.Equal("HKLM\\X\u0001name", StartupManagerTool.KeyOf(@"HKLM\X", "name"));
    }

    [Fact]
    public void DisabledStore_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            var records = new List<DisabledRecord>
            {
                DisabledRecord.FromEntry(new AutorunsEntry
                {
                    EntryLocation = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    Entry = "Foo",
                    Category = "Logon",
                    ImagePath = @"C:\x\foo.exe"
                }, "registry")
            };
            records[0].Payload = "bar.exe -x";
            records[0].Deleted = true; // 删除（已备份）也被持久化，重启后可继续恢复
            StartupDisabledStore.Save(records, path);

            var loaded = StartupDisabledStore.Load(path);
            Assert.Single(loaded);
            Assert.Equal(records[0].Location, loaded[0].Location);
            Assert.Equal(records[0].Entry, loaded[0].Entry);
            Assert.Equal("registry", loaded[0].Kind);
            Assert.Equal("bar.exe -x", loaded[0].Payload);
            Assert.True(loaded[0].Deleted);
            Assert.False(loaded[0].ToEntry().IsEnabled);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void DeleteFileToRecycleBin_MovesFileAwayInsteadOfHardDelete()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        File.WriteAllText(file, "可在回收站找回");
        try
        {
            StartupActionService.DeleteFileToRecycleBin(file);
            Assert.False(File.Exists(file)); // 原位置已不存在（进了回收站，而非彻底删除）
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    [Fact]
    public void DisabledStore_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(StartupDisabledStore.Load(Path.Combine(Path.GetTempPath(), "不存在_" + Guid.NewGuid().ToString("N") + ".json")));
    }
}

/// <summary>注册测试：与 BuiltinToolRegistryTests 共享集合，避免反射清空注册表时并行冲突。</summary>
[Collection("BuiltinToolRegistry")]
public class StartupManagerRegistrationTests
{
    private static void ClearRegistry()
    {
        var list = (List<IBuiltinTool>)typeof(BuiltinToolRegistry)
            .GetField("_tools", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        list.Clear();
    }

    [Fact]
    public void RegisterDefaults_ContainsStartupManager()
    {
        ClearRegistry();
        BuiltinToolRegistry.RegisterDefaults();
        var tool = BuiltinToolRegistry.GetById("startup-manager");
        Assert.NotNull(tool);
        Assert.Equal("启动项管理", tool.Name);
        Assert.Equal("系统工具", tool.Category);
        Assert.Equal(BuiltinToolKind.Dialog, tool.Kind);
    }
}