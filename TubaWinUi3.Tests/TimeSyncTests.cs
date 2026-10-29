using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 时间同步内置工具：预设清单、对等列表拼装、地址校验、SNTP 数学、
/// w32tm 输出解析与健康检查判定（全部为纯逻辑，不触碰系统配置）。
/// </summary>
public class TimeSyncTests
{
    // ══════════════════ 预设与拼装 ══════════════════

    [Fact]
    public void Presets_AreWellFormed()
    {
        Assert.NotEmpty(TimeSyncCatalog.Presets);
        Assert.Equal(TimeSyncCatalog.Presets.Count, TimeSyncCatalog.Presets.Select(p => p.Id).Distinct().Count());

        foreach (var preset in TimeSyncCatalog.Presets)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
            Assert.False(string.IsNullOrWhiteSpace(preset.Summary));
            Assert.False(string.IsNullOrWhiteSpace(preset.Note));
            Assert.NotEmpty(preset.Hosts);
            foreach (var host in preset.Hosts)
            {
                Assert.Null(TimeSyncCatalog.ValidateHost(host));
            }
        }
    }

    [Theory]
    [InlineData("aliyun", "ntp.aliyun.com")]
    [InlineData("tencent", "ntp.tencent.com")]
    [InlineData("ntsc", "ntp.ntsc.ac.cn")]
    [InlineData("microsoft", "time.windows.com")]
    public void FindPreset_ReturnsConfiguredPreset(string id, string firstHost)
    {
        var preset = TimeSyncCatalog.FindPreset(id);
        Assert.NotNull(preset);
        Assert.Equal(firstHost, preset!.Hosts[0]);
    }

    [Fact]
    public void BuildPeerList_UsesClientFlag()
    {
        var list = TimeSyncCatalog.BuildPeerList(["ntp.aliyun.com", "ntp1.aliyun.com"]);
        Assert.Equal("ntp.aliyun.com,0x8 ntp1.aliyun.com,0x8", list);
    }

    [Fact]
    public void BuildPeerList_AddsSpecialIntervalFlag()
    {
        var list = TimeSyncCatalog.BuildPeerList(["time.windows.com"], useSpecialInterval: true);
        Assert.Equal("time.windows.com,0x9", list);
    }

    [Fact]
    public void BuildPeerList_TrimsDeduplicatesAndStripsExistingFlags()
    {
        var list = TimeSyncCatalog.BuildPeerList([" ntp.aliyun.com,0x8 ", "NTP.ALIYUN.COM", "", "  ", "ntp1.aliyun.com"]);
        Assert.Equal("ntp.aliyun.com,0x8 ntp1.aliyun.com,0x8", list);
    }

    [Theory]
    [InlineData("ntp.aliyun.com ntp1.aliyun.com", 2)]
    [InlineData("ntp.aliyun.com,ntp1.aliyun.com", 2)]
    [InlineData("ntp.aliyun.com，ntp1.aliyun.com；ntp2.aliyun.com", 3)]
    [InlineData("ntp.aliyun.com,0x9", 1)]
    [InlineData("", 0)]
    public void SplitHosts_HandlesSeparators(string raw, int expected)
    {
        Assert.Equal(expected, TimeSyncCatalog.SplitHosts(raw).Count);
    }

    [Fact]
    public void SplitHosts_StripsFlagsAndKeepsOrder()
    {
        var hosts = TimeSyncCatalog.SplitHosts("ntp.aliyun.com,0x9 ntp1.aliyun.com,0x8");
        Assert.Equal(new[] { "ntp.aliyun.com", "ntp1.aliyun.com" }, hosts);
    }

    [Theory]
    [InlineData("ntp.aliyun.com")]
    [InlineData("203.107.6.88")]
    [InlineData("time.windows.com")]
    [InlineData("a-b.c_d.example.org")]
    public void ValidateHost_Accepts(string host) => Assert.Null(TimeSyncCatalog.ValidateHost(host));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://ntp.aliyun.com")]
    [InlineData("ntp.aliyun.com:123")]
    [InlineData("ntp.aliyun.com/path")]
    [InlineData("a..b.com")]
    [InlineData(".ntp.aliyun.com")]
    [InlineData("ntp aliyun.com")]
    [InlineData("ntp服务器.com")]
    public void ValidateHost_Rejects(string host) => Assert.NotNull(TimeSyncCatalog.ValidateHost(host));

    [Fact]
    public void ValidateHosts_ReportsOffendingEntry()
    {
        var error = TimeSyncCatalog.ValidateHosts(["ntp.aliyun.com", "http://x.com"]);
        Assert.NotNull(error);
        Assert.Contains("http://x.com", error!);
    }

    // ══════════════════ 注册表字符串反查 ══════════════════

    [Theory]
    [InlineData("ntp.aliyun.com,0x8 ntp1.aliyun.com,0x8", "aliyun")]
    [InlineData("time.windows.com,0x9", "microsoft")]
    [InlineData("ntp.tencent.com,0x8", "tencent")]
    public void MatchPreset_RecognizesRegistryPeers(string peers, string expectedId)
        => Assert.Equal(expectedId, TimeSyncCatalog.MatchPreset(peers)?.Id);

    [Fact]
    public void MatchPreset_ReturnsNullForUnknownPeers()
        => Assert.Null(TimeSyncCatalog.MatchPreset("time.example.org,0x8"));

    [Theory]
    [InlineData("ntp.aliyun.com,0x8 ntp1.aliyun.com,0x8", "ntp.aliyun.com", 2)]
    [InlineData("time.windows.com,0x9", "time.windows.com", 1)]
    [InlineData("", null, 0)]
    [InlineData("   ", null, 0)]
    public void FirstPeerAndCount(string peers, string? expectedFirst, int expectedCount)
    {
        Assert.Equal(expectedFirst, TimeSyncCatalog.FirstPeer(peers));
        Assert.Equal(expectedCount, TimeSyncCatalog.CountPeers(peers));
    }

    [Fact]
    public void DescribeInterval_FallsBackToSeconds()
    {
        Assert.Equal("每小时", TimeSyncCatalog.DescribeInterval(3600));
        Assert.Equal("系统自适应（推荐）", TimeSyncCatalog.DescribeInterval(0));
        Assert.Equal("7200 秒", TimeSyncCatalog.DescribeInterval(7200));
    }

    // ══════════════════ SNTP 协议 ══════════════════

    [Fact]
    public void BuildRequest_ProducesClientPacketWithTransmitTimestamp()
    {
        var sent = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).AddMilliseconds(250);
        var packet = TimeSyncProbe.BuildRequest(sent);

        Assert.Equal(48, packet.Length);
        Assert.Equal(0x1B, packet[0]); // LI=0 / VN=4 / Mode=3
        Assert.All(packet.Skip(1).Take(39), b => Assert.Equal((byte)0, b));

        var transmit = ReadTimestamp(packet, 40);
        Assert.Equal(1_700_000_000.25 + TimeSyncProbe.NtpEpochOffsetSeconds, transmit, 3);
    }

    [Fact]
    public void ReadResponse_ComputesOffsetAndRoundTrip()
    {
        const double serverTime = 1_700_000_000d;
        const double oneWayDelay = 0.05;   // 单程 50 ms
        const double clockOffset = 0.25;   // 服务器比本机快 250 ms（本机慢）

        var sent = DateTimeOffset.UnixEpoch.AddSeconds(serverTime - oneWayDelay - clockOffset);
        var received = DateTimeOffset.UnixEpoch.AddSeconds(serverTime + oneWayDelay - clockOffset);

        var packet = BuildServerPacket(serverTime, serverTime);
        var result = TimeSyncProbe.ReadResponse(packet, sent, received, "ntp.test");

        Assert.True(result.Ok);
        Assert.Equal("ntp.test", result.Host);
        Assert.Equal(2, result.Stratum);
        Assert.Equal(250, result.OffsetMs, 2);
        Assert.Equal(100, result.RoundTripMs, 2);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds((long)serverTime), result.ServerTimeUtc);
    }

    [Fact]
    public void ReadResponse_DetectsClockAheadOfServer()
    {
        const double serverTime = 1_700_000_000d;
        const double oneWayDelay = 0.05;
        // 本机比服务器快 0.5 秒：本地收发时间戳都比真实时刻大 0.5
        var sent = DateTimeOffset.UnixEpoch.AddSeconds(serverTime - oneWayDelay + 0.5);
        var received = DateTimeOffset.UnixEpoch.AddSeconds(serverTime + oneWayDelay + 0.5);

        var result = TimeSyncProbe.ReadResponse(BuildServerPacket(serverTime, serverTime), sent, received, "ntp.test");

        Assert.True(result.Ok);
        Assert.Equal(-500, result.OffsetMs, 2);
        Assert.Contains("本机快", TimeSyncService.FormatOffset(result.OffsetMs));
    }

    [Fact]
    public void ReadResponse_RejectsMalformedPackets()
    {
        var packet = BuildServerPacket(1_700_000_000d, 1_700_000_000d);
        var now = DateTimeOffset.UtcNow;

        Assert.False(TimeSyncProbe.ReadResponse(new byte[10], now, now, "h").Ok);

        var wrongMode = BuildServerPacket(1_700_000_000d, 1_700_000_000d);
        wrongMode[0] = 0x1B; // Mode=3（请求）不是响应
        Assert.False(TimeSyncProbe.ReadResponse(wrongMode, now, now, "h").Ok);

        var kissOfDeath = BuildServerPacket(1_700_000_000d, 1_700_000_000d, stratum: 0);
        Assert.Contains("拒绝服务", TimeSyncProbe.ReadResponse(kissOfDeath, now, now, "h").Error!);

        var bogus = BuildServerPacket(1_700_000_000d, 1_700_000_000d);
        var farFuture = now.AddDays(30);
        Assert.False(TimeSyncProbe.ReadResponse(bogus, farFuture, farFuture.AddSeconds(1), "h").Ok);
    }

    [Fact]
    public async Task QueryAsync_InvalidHostFails()
    {
        var result = await TimeSyncProbe.QueryAsync("", TimeSpan.FromMilliseconds(200));
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    // ══════════════════ w32tm 输出解析 ══════════════════

    [Fact]
    public void ParseSource_TakesFirstMeaningfulLine()
    {
        Assert.Equal("ntp.aliyun.com", W32TimeCli.ParseSource("ntp.aliyun.com\r\n"));
        Assert.Equal("本地 CMOS 时钟", W32TimeCli.ParseSource("\r\n本地 CMOS 时钟\r\n"));
        Assert.Equal("", W32TimeCli.ParseSource("   \r\n\r\n"));
    }

    [Theory]
    [InlineData("Local CMOS Clock", true)]
    [InlineData("本地 CMOS 时钟", true)]
    [InlineData("Free-running System Clock", true)]
    [InlineData("ntp.aliyun.com", false)]
    [InlineData("VM IC Time Synchronization Provider", false)]
    public void LooksLikeLocalClock_DetectsUnsyncedSources(string source, bool expected)
        => Assert.Equal(expected, W32TimeCli.LooksLikeLocalClock(source));

    [Fact]
    public void ParseLastSyncTime_HandlesChineseStatusOutput()
    {
        const string output = """
        跳跃指示器: 0(无警告)
        层次: 2 (次引用 - 已通过 IPv4 同步)
        精度: -23 (每刻度 119.209ns)
        根延迟: 0.0009258s
        引用 ID: 0xC0000036 (未知)
        上次成功同步时间: 2026/9/15 10:00:00
        源: ntp.aliyun.com
        轮询间隔: 10 (1024s)
        相位偏移: 0.0001886s
        上次同步错误: 0 (操作成功完成。)
        """;

        var parsed = W32TimeCli.ParseLastSyncTime(output);
        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(2026, 9, 15, 10, 0, 0), parsed!.Value.DateTime);
    }

    [Fact]
    public void ParseLastSyncTime_HandlesEnglishStatusOutput()
    {
        const string output = """
        Leap Indicator: 0(no warning)
        Stratum: 1 (primary reference - syncd by radio clock)
        Last Successful Sync Time: 9/15/2026 10:00:00 AM
        Source: time.windows.com
        Poll Interval: 6 (64s)
        """;

        var parsed = W32TimeCli.ParseLastSyncTime(output);
        Assert.NotNull(parsed);
        Assert.Equal(10, parsed!.Value.Hour);
    }

    [Theory]
    [InlineData("")]
    [InlineData("跳跃指示器: 0(无警告)\n层次: 2 (次引用)")]
    [InlineData("上次同步错误: 0 (操作成功完成。)")]
    public void ParseLastSyncTime_ReturnsNullWhenAbsent(string output)
        => Assert.Null(W32TimeCli.ParseLastSyncTime(output));

    /// <summary>真实机器输出（Windows 11 中文版，未提权）：/query /source 会拒绝访问，只能从 /status 取时间源。</summary>
    private const string RealStatusOutput = """
        Leap 指示符: 0(无警告)
        层次: 5 (次引用 - 与(S)NTP 同步)
        精度: -23 (每刻度 119.209ns)
        根延迟: 0.1343173s
        根分散: 7.9028269s
        引用 ID: 0x34E772B7 (源 IP:  52.231.114.183)
        上次成功同步时间: 2026/9/16 0:22:45
        源: time.windows.com,0x9 
        轮询间隔: 10 (1024s)
        """;

    [Fact]
    public void ParseSourceFromStatus_ReadsRealChineseOutput()
        => Assert.Equal("time.windows.com,0x9", W32TimeCli.ParseSourceFromStatus(RealStatusOutput));

    [Fact]
    public void ParseLastSyncTime_ReadsRealChineseOutput()
    {
        var parsed = W32TimeCli.ParseLastSyncTime(RealStatusOutput);
        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(2026, 9, 16, 0, 22, 45), parsed!.Value.DateTime);
    }

    [Fact]
    public void RealStatusSource_MatchesWindowsDefaultPreset()
    {
        var source = W32TimeCli.ParseSourceFromStatus(RealStatusOutput);
        Assert.Equal("time.windows.com", TimeSyncCatalog.FirstPeer(source));
        Assert.Equal(1, TimeSyncCatalog.CountPeers(source));
        Assert.Equal("microsoft", TimeSyncCatalog.MatchPreset(source)?.Id);
    }

    [Fact]
    public void ParseSourceFromStatus_IgnoresReferenceIdLineAndEmptyOutput()
    {
        Assert.Equal("", W32TimeCli.ParseSourceFromStatus("引用 ID: 0x34E772B7 (源 IP: 52.231.114.183)"));
        Assert.Equal("", W32TimeCli.ParseSourceFromStatus(""));
        Assert.Equal("Local CMOS Clock", W32TimeCli.ParseSourceFromStatus("Source: Local CMOS Clock"));
    }

    [Fact]
    public void DescribeExitCode_ExplainsCommonCodes()
    {
        Assert.Contains("管理员", W32TimeCli.DescribeExitCode(5));
        Assert.Equal("成功", W32TimeCli.DescribeExitCode(0));
    }

    // ══════════════════ 健康检查与报告 ══════════════════

    private static TimeSyncSnapshot HealthySnapshot() => new()
    {
        Service = new TimeServiceInfo(true, true, "Auto", true),
        NtpClient = new NtpClientInfo(true, "NTP", "ntp.aliyun.com,0x8", 3600),
        CurrentSource = "ntp.aliyun.com",
        LastSyncTime = DateTimeOffset.Now.AddMinutes(-5),
        SslTimeSeedEnabled = true,
        DomainJoined = false,
        IsAdmin = true,
    };

    [Fact]
    public void Evaluate_HealthyStateHasNoIssues()
    {
        var issues = TimeSyncService.Evaluate(HealthySnapshot(), new NtpProbeResult { Ok = true, Host = "ntp.aliyun.com", OffsetMs = 12 });
        Assert.Empty(issues);
    }

    [Fact]
    public void Evaluate_NotAdminIsCritical()
    {
        var issues = TimeSyncService.Evaluate(HealthySnapshot() with { IsAdmin = false }, null);
        Assert.Contains(issues, i => i.Level == TimeSyncIssueLevel.Critical && i.Title.Contains("管理员"));
    }

    [Fact]
    public void Evaluate_StoppedServiceIsReported()
    {
        var snapshot = HealthySnapshot() with { Service = new TimeServiceInfo(true, false, "Auto", true) };
        var issues = TimeSyncService.Evaluate(snapshot, null);
        Assert.Contains(issues, i => i.Title.Contains("没有运行"));
    }

    [Fact]
    public void Evaluate_DisabledServiceIsCritical()
    {
        var snapshot = HealthySnapshot() with { Service = new TimeServiceInfo(true, false, "Disabled", false) };
        var issues = TimeSyncService.Evaluate(snapshot, null);
        Assert.Contains(issues, i => i.Level == TimeSyncIssueLevel.Critical && i.Title.Contains("禁用"));
    }

    [Fact]
    public void Evaluate_ManualStartModeIsWarned()
    {
        var snapshot = HealthySnapshot() with { Service = new TimeServiceInfo(true, true, "Manual", false) };
        var issues = TimeSyncService.Evaluate(snapshot, null);
        Assert.Contains(issues, i => i.Title.Contains("手动启动"));
    }

    [Fact]
    public void Evaluate_NtpClientDisabledIsWarned()
    {
        var snapshot = HealthySnapshot() with { NtpClient = new NtpClientInfo(false, "NTP", "", 0) };
        var issues = TimeSyncService.Evaluate(snapshot, null);
        Assert.Contains(issues, i => i.Title.Contains("NTP 客户端"));
    }

    [Fact]
    public void Evaluate_DomainHierarchyOnWorkgroupIsWarned()
    {
        var snapshot = HealthySnapshot() with { NtpClient = new NtpClientInfo(true, "NT5DS", "", 0), DomainJoined = false };
        var issues = TimeSyncService.Evaluate(snapshot, null);
        Assert.Contains(issues, i => i.Title.Contains("NT5DS"));
    }

    [Fact]
    public void Evaluate_LocalClockSourceIsWarned()
    {
        var snapshot = HealthySnapshot() with { CurrentSource = "本地 CMOS 时钟" };
        var issues = TimeSyncService.Evaluate(snapshot, null);
        Assert.Contains(issues, i => i.Title.Contains("硬件时钟"));
    }

    [Fact]
    public void Evaluate_StaleLastSyncIsWarned()
    {
        var snapshot = HealthySnapshot() with { LastSyncTime = DateTimeOffset.Now.AddDays(-10) };
        var issues = TimeSyncService.Evaluate(snapshot, null);
        Assert.Contains(issues, i => i.Title.Contains("很久"));
    }

    [Fact]
    public void Evaluate_LargeDriftIsCritical()
    {
        var probe = new NtpProbeResult { Ok = true, Host = "ntp.aliyun.com", OffsetMs = 120_000 };
        var issues = TimeSyncService.Evaluate(HealthySnapshot(), probe);
        Assert.Contains(issues, i => i.Level == TimeSyncIssueLevel.Critical);
    }

    [Fact]
    public void Evaluate_UnreachableServerIsWarned()
    {
        var probe = NtpProbeResult.Fail("time.windows.com", "没有响应");
        var issues = TimeSyncService.Evaluate(HealthySnapshot(), probe);
        Assert.Contains(issues, i => i.Title.Contains("不可达"));
    }

    [Theory]
    [InlineData(250, "本机慢 250 毫秒")]
    [InlineData(-250, "本机快 250 毫秒")]
    [InlineData(2500, "本机慢 2.50 秒")]
    public void FormatOffset_DescribesDirection(double offsetMs, string expected)
        => Assert.Equal(expected, TimeSyncService.FormatOffset(offsetMs));

    [Fact]
    public void BuildReport_ContainsKeySections()
    {
        var snapshot = HealthySnapshot() with
        {
            RawStatus = "源: ntp.aliyun.com",
            RawConfiguration = "[配置]\r\nNtpServer: ntp.aliyun.com,0x8",
        };
        var report = TimeSyncService.BuildDiagnosticReport(snapshot,
            [new NtpProbeResult { Ok = true, Host = "ntp.aliyun.com", Stratum = 2, OffsetMs = 12, RoundTripMs = 90 }]);

        Assert.Contains("时间同步诊断报告", report);
        Assert.Contains("Windows 时间服务", report);
        Assert.Contains("NTP 客户端", report);
        Assert.Contains("ntp.aliyun.com,0x8", report);
        Assert.Contains("w32tm /query /status", report);
        Assert.Contains("层级 2", report);
    }

    // ══════════════════ 设置存储 ══════════════════

    [Fact]
    public void Settings_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tubawinui3-tests", Guid.NewGuid().ToString("N"));
        var previous = TimeSyncCatalog.DataDirOverride;
        TimeSyncCatalog.DataDirOverride = dir;

        try
        {
            var settings = new TimeSyncSettings
            {
                PresetId = "aliyun",
                CustomHosts = "ntp.example.org",
                SyncIntervalSeconds = 21600,
                SslTimeSeedDisabled = true,
            };
            TimeSyncCatalog.SaveSettings(settings);

            var loaded = TimeSyncCatalog.LoadSettings();
            Assert.Equal("aliyun", loaded.PresetId);
            Assert.Equal("ntp.example.org", loaded.CustomHosts);
            Assert.Equal(21600, loaded.SyncIntervalSeconds);
            Assert.True(loaded.SslTimeSeedDisabled);
        }
        finally
        {
            TimeSyncCatalog.DataDirOverride = previous;
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Settings_MissingFileReturnsDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tubawinui3-tests", Guid.NewGuid().ToString("N"));
        var previous = TimeSyncCatalog.DataDirOverride;
        TimeSyncCatalog.DataDirOverride = dir;

        try
        {
            var loaded = TimeSyncCatalog.LoadSettings();
            Assert.Equal("", loaded.PresetId);
            Assert.Equal(0, loaded.SyncIntervalSeconds);
            Assert.False(loaded.SslTimeSeedDisabled);
        }
        finally
        {
            TimeSyncCatalog.DataDirOverride = previous;
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // ══════════════════ 测试辅助 ══════════════════

    /// <summary>按 NTP 报文布局构造一个响应包（stratum 2 / mode 4，收发时间戳相同）。</summary>
    private static byte[] BuildServerPacket(double receiveSeconds, double transmitSeconds, int stratum = 2)
    {
        var packet = new byte[48];
        packet[0] = 0x24; // LI=0 / VN=4 / Mode=4（服务器）
        packet[1] = (byte)stratum;
        packet[2] = 4;
        packet[3] = 0xEC;
        WriteTimestamp(packet, 16, receiveSeconds - 1);
        WriteTimestamp(packet, 32, receiveSeconds);
        WriteTimestamp(packet, 40, transmitSeconds);
        return packet;
    }

    private static double ReadTimestamp(byte[] packet, int offset)
    {
        ulong seconds = ((ulong)packet[offset] << 24) | ((ulong)packet[offset + 1] << 16)
            | ((ulong)packet[offset + 2] << 8) | packet[offset + 3];
        ulong fraction = ((ulong)packet[offset + 4] << 24) | ((ulong)packet[offset + 5] << 16)
            | ((ulong)packet[offset + 6] << 8) | packet[offset + 7];
        return seconds + fraction / 4294967296d;
    }

    private static void WriteTimestamp(byte[] packet, int offset, double unixSeconds)
    {
        var ntpSeconds = unixSeconds + TimeSyncProbe.NtpEpochOffsetSeconds;
        var seconds = (ulong)Math.Floor(ntpSeconds);
        var fraction = (ulong)Math.Round((ntpSeconds - seconds) * 4294967296d);
        if (fraction == 4294967296ul) { seconds++; fraction = 0; }

        packet[offset] = (byte)(seconds >> 24);
        packet[offset + 1] = (byte)(seconds >> 16);
        packet[offset + 2] = (byte)(seconds >> 8);
        packet[offset + 3] = (byte)seconds;
        packet[offset + 4] = (byte)(fraction >> 24);
        packet[offset + 5] = (byte)(fraction >> 16);
        packet[offset + 6] = (byte)(fraction >> 8);
        packet[offset + 7] = (byte)fraction;
    }
}
