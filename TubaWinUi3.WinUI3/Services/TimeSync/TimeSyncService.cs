using System.Diagnostics;
using System.Management;
using System.Security.Principal;
using Microsoft.Win32;

namespace TubaWinUi3.Services;

/// <summary>Windows 时间服务（w32time）的运行状态与启动类型。</summary>
public sealed record TimeServiceInfo(bool Exists, bool Running, string StartMode, bool DelayedAutoStart)
{
    public bool StartModeKnown => StartMode.Length > 0;
    /// <summary>启动类型中文名；未知时返回「未知」。</summary>
    public string StartModeText => StartMode switch
    {
        "Auto" => DelayedAutoStart ? "自动（延迟启动）" : "自动",
        "Manual" => "手动",
        "Disabled" => "已禁用",
        _ => "未知"
    };
}

/// <summary>NTP 客户端的注册表配置。</summary>
public sealed record NtpClientInfo(bool Enabled, string SyncType, string Peers, int SpecialPollInterval)
{
    public string SyncTypeText => SyncType.ToUpperInvariant() switch
    {
        "NT5DS" => "域层次（NT5DS）",
        "NTP" => "手动指定的时间源（NTP）",
        "ALLSYNC" => "所有来源（AllSync）",
        "NOSYNC" => "不同步（NoSync）",
        _ => SyncType.Length > 0 ? SyncType : "未配置"
    };
}

/// <summary>一次状态读取的全部结果。</summary>
public sealed record TimeSyncSnapshot
{
    public TimeServiceInfo Service { get; init; } = new(false, false, "", false);
    public NtpClientInfo NtpClient { get; init; } = new(false, "", "", 0);
    public string CurrentSource { get; init; } = "";
    public DateTimeOffset? LastSyncTime { get; init; }
    public bool SslTimeSeedEnabled { get; init; }
    public bool DomainJoined { get; init; }
    public bool IsAdmin { get; init; }
    public string? RawStatus { get; init; }
    public string? RawConfiguration { get; init; }
    public bool ServiceQueryFailed { get; init; }

    public NtpPreset? MatchedPreset => TimeSyncCatalog.MatchPreset(NtpClient.Peers);
    public bool UsingDomainHierarchy => NtpClient.SyncType.Equals("NT5DS", StringComparison.OrdinalIgnoreCase);
    public bool SourceIsLocalClock => W32TimeCli.LooksLikeLocalClock(CurrentSource);
}

public enum TimeSyncIssueLevel { Info, Warning, Critical }

public sealed record TimeSyncIssue(string Title, string Detail, TimeSyncIssueLevel Level);

public sealed record TimeSyncActionResult(bool Ok, string Message, string Detail = "")
{
    public static TimeSyncActionResult Success(string message, string detail = "") => new(true, message, detail);
    public static TimeSyncActionResult Failure(string message, string detail = "") => new(false, message, detail);
}

/// <summary>
/// 系统时间同步编排：读状态（注册表 + WMI + w32tm）、切换 NTP 服务器、立即校时、
/// 恢复系统默认、服务控制与一键修复。
/// 官方依据：https://learn.microsoft.com/windows-server/networking/windows-time-service/windows-time-service-tools-and-settings
/// </summary>
public static class TimeSyncService
{
    private const string W32TimeKey = @"SYSTEM\CurrentControlSet\Services\W32Time";
    private const string ParametersKey = W32TimeKey + @"\Parameters";
    private const string NtpClientKey = W32TimeKey + @"\TimeProviders\NtpClient";
    private const string ConfigKey = W32TimeKey + @"\Config";
    private const string ServiceName = "w32time";

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConfigTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ResyncTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(25);

    public static bool IsAdmin
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    // ══════════════════ 状态读取 ══════════════════

    public static async Task<TimeSyncSnapshot> GetSnapshotAsync(bool includeConfiguration = false, CancellationToken ct = default)
    {
        var registry = ReadRegistry();

        var (service, serviceFailed) = await ReadServiceAsync(ct).ConfigureAwait(false);
        var domainJoined = await IsDomainJoinedAsync(ct).ConfigureAwait(false);

        string currentSource = "";
        DateTimeOffset? lastSync = null;
        string? rawStatus = null;
        string? rawConfiguration = null;

        if (service.Running)
        {
            // /status 一次就能拿到「上次成功同步时间」和「当前时间源」，且未提权时也可用
            var status = await W32TimeCli.W32TmAsync(QueryTimeout, "/query", "/status").ConfigureAwait(false);
            rawStatus = status.Combined.Trim();
            lastSync = W32TimeCli.ParseLastSyncTime(status.StdOut);
            currentSource = W32TimeCli.ParseSourceFromStatus(status.StdOut);

            if (currentSource.Length == 0)
            {
                var source = await W32TimeCli.W32TmAsync(QueryTimeout, "/query", "/source").ConfigureAwait(false);
                if (source.Ok) currentSource = W32TimeCli.ParseSource(source.StdOut);
            }

            if (includeConfiguration)
            {
                var configuration = await W32TimeCli.W32TmAsync(QueryTimeout, "/query", "/configuration")
                    .ConfigureAwait(false);
                rawConfiguration = configuration.Ok
                    ? configuration.Combined.Trim()
                    : $"（读取失败：{configuration.FirstLine()}）";
            }
        }
        else
        {
            var status = await W32TimeCli.W32TmAsync(QueryTimeout, "/query", "/status").ConfigureAwait(false);
            rawStatus = status.Ok ? status.Combined.Trim() : $"（读取失败：{status.FirstLine()}）";
        }

        return new TimeSyncSnapshot
        {
            Service = service,
            NtpClient = registry.NtpClient,
            CurrentSource = currentSource,
            LastSyncTime = lastSync,
            SslTimeSeedEnabled = registry.SslTimeSeedEnabled,
            DomainJoined = domainJoined,
            IsAdmin = IsAdmin,
            RawStatus = rawStatus,
            RawConfiguration = rawConfiguration,
            ServiceQueryFailed = serviceFailed,
        };
    }

    private sealed record RegistryState(NtpClientInfo NtpClient, bool SslTimeSeedEnabled);

    private static RegistryState ReadRegistry()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

        var enabled = false;
        var syncType = "";
        var peers = "";
        var pollInterval = 0;

        using (var key = hklm.OpenSubKey(NtpClientKey))
        {
            enabled = key?.GetValue("Enabled") is int value && value != 0;
            pollInterval = key?.GetValue("SpecialPollInterval") as int? ?? 0;
        }

        using (var key = hklm.OpenSubKey(ParametersKey))
        {
            syncType = key?.GetValue("Type") as string ?? "";
            peers = key?.GetValue("NtpServer") as string ?? "";
        }

        var sslSeed = true;
        using (var key = hklm.OpenSubKey(ConfigKey))
        {
            sslSeed = key?.GetValue("UtilizeSslTimeData") is not int value || value != 0;
        }

        return new RegistryState(new NtpClientInfo(enabled, syncType, peers, pollInterval), sslSeed);
    }

    /// <summary>服务状态优先取 WMI（值恒为英文，不受系统语言影响），失败时退回注册表启动类型。</summary>
    private static async Task<(TimeServiceInfo Info, bool Failed)> ReadServiceAsync(CancellationToken ct)
    {
        try
        {
            var result = await Task.Run(() =>
            {
                using var searcher = new ManagementObjectSearcher($"SELECT Name, State, StartMode, DelayedAutoStart FROM Win32_Service WHERE Name='{ServiceName}'");
                foreach (var item in searcher.Get().Cast<ManagementObject>())
                {
                    using (item)
                    {
                        var state = item["State"] as string ?? "";
                        var startMode = item["StartMode"] as string ?? "";
                        var delayed = item["DelayedAutoStart"] is bool flag && flag;
                        return new TimeServiceInfo(true, state.Equals("Running", StringComparison.OrdinalIgnoreCase), startMode, delayed);
                    }
                }
                return new TimeServiceInfo(false, false, "", false);
            }, ct).ConfigureAwait(false);

            return (result, false);
        }
        catch
        {
            // WMI 不可用时退回注册表（启动类型可信，运行状态未知按停止处理）
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(W32TimeKey);
            if (key is null) return (new TimeServiceInfo(false, false, "", false), true);

            var start = key.GetValue("Start") as int? ?? -1;
            var delayed = key.GetValue("DelayedAutostart") as int? == 1;
            var mode = start switch { 0 or 1 or 2 => "Auto", 3 => "Manual", 4 => "Disabled", _ => "" };
            return (new TimeServiceInfo(true, false, mode, delayed), true);
        }
    }

    private static async Task<bool> IsDomainJoinedAsync(CancellationToken ct)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT PartOfDomain FROM Win32_ComputerSystem");
                foreach (var item in searcher.Get().Cast<ManagementObject>())
                {
                    using (item)
                    {
                        return item["PartOfDomain"] is bool joined && joined;
                    }
                }
                return false;
            }, ct).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    // ══════════════════ 应用 / 校时 ══════════════════

    /// <summary>切换 NTP 服务器：写配置 → 重启时间服务 → 立即校时。</summary>
    public static async Task<TimeSyncActionResult> ApplyServersAsync(IReadOnlyList<string> hosts, int specialPollIntervalSeconds, CancellationToken ct = default)
    {
        var error = TimeSyncCatalog.ValidateHosts(hosts);
        if (error is not null) return TimeSyncActionResult.Failure("地址不合法", error);

        var useSpecialInterval = specialPollIntervalSeconds > 0;
        var peerList = TimeSyncCatalog.BuildPeerList(hosts, useSpecialInterval);
        var steps = new List<string>();

        var config = await W32TimeCli.W32TmAsync(ConfigTimeout,
            "/config", $"/manualpeerlist:{peerList}", "/syncfromflags:manual", "/update").ConfigureAwait(false);
        steps.Add($"w32tm /config → {DescribeResult(config)}");
        if (!config.Ok)
            return TimeSyncActionResult.Failure("写入 NTP 配置失败", string.Join('\n', steps) + "\n" + config.FirstLine());

        if (useSpecialInterval)
        {
            var interval = WriteRegistryDword(NtpClientKey, "SpecialPollInterval", specialPollIntervalSeconds);
            steps.Add(interval.Ok
                ? $"同步间隔设为 {TimeSyncCatalog.DescribeInterval(specialPollIntervalSeconds)}"
                : $"同步间隔写入失败：{interval.Message}");
        }

        var restart = await RestartServiceAsync(ct).ConfigureAwait(false);
        steps.Add($"重启时间服务 → {DescribeResult(restart)}");

        var resync = await ResyncAsync(ct).ConfigureAwait(false);
        steps.Add($"立即校时 → {DescribeResult(resync)}");

        var detail = string.Join('\n', steps);
        if (!resync.Ok)
            return TimeSyncActionResult.Failure("配置已写入，但本次校时没成功", detail);
        if (!restart.Ok)
            return TimeSyncActionResult.Failure("配置已写入，但时间服务重启失败", detail);

        return TimeSyncActionResult.Success($"已切换到 {TimeSyncCatalog.FirstPeer(peerList) ?? "新时间源"} 并完成校时", detail);
    }

    /// <summary>立即校时（w32tm /resync /rediscover：重新探测网络源后再同步，官方文档开关）。</summary>
    public static async Task<TimeSyncActionResult> ResyncAsync(CancellationToken ct = default)
    {
        var result = await W32TimeCli.W32TmAsync(ResyncTimeout, "/resync", "/rediscover").ConfigureAwait(false);
        if (result.Ok && !result.Combined.Contains("失败", StringComparison.Ordinal))
            return TimeSyncActionResult.Success("校时命令已发送，本机时间已与时间源对齐", result.Combined.Trim());

        return TimeSyncActionResult.Failure("校时失败：" + W32TimeCli.DescribeExitCode(result.ExitCode), result.Combined.Trim());
    }

    /// <summary>
    /// 恢复系统默认：域成员回到域层次（NT5DS），非域机器回到 time.windows.com,0x9（出厂配置）。
    /// 不动同步频率——SpecialPollInterval 各版本出厂值并不一致，改了反而是"发明"配置。
    /// </summary>
    public static async Task<TimeSyncActionResult> RestoreDefaultAsync(bool domainJoined, CancellationToken ct = default)
    {
        var steps = new List<string>();
        var args = domainJoined
            ? new[] { "/config", "/syncfromflags:domhier", "/update" }
            : new[] { "/config", $"/manualpeerlist:{TimeSyncCatalog.BuildPeerList((string[])["time.windows.com"], useSpecialInterval: true)}", "/syncfromflags:manual", "/update" };

        var config = await W32TimeCli.W32TmAsync(ConfigTimeout, args).ConfigureAwait(false);
        steps.Add($"{W32TimeCli.Describe("w32tm", args)} → {DescribeResult(config)}");
        if (!config.Ok)
            return TimeSyncActionResult.Failure("恢复默认时间源失败", string.Join('\n', steps) + "\n" + config.FirstLine());

        var restart = await RestartServiceAsync(ct).ConfigureAwait(false);
        steps.Add($"重启时间服务 → {DescribeResult(restart)}");

        var resync = await ResyncAsync(ct).ConfigureAwait(false);
        steps.Add($"立即校时 → {DescribeResult(resync)}");

        var detail = string.Join('\n', steps);
        var target = domainJoined ? "域时间层次" : "time.windows.com（系统默认）";
        return config.Ok
            ? TimeSyncActionResult.Success($"已恢复为{target}", detail)
            : TimeSyncActionResult.Failure("恢复默认失败", detail);
    }

    // ══════════════════ 服务控制 ══════════════════

    public static async Task<TimeSyncActionResult> RestartServiceAsync(CancellationToken ct = default)
    {
        var stop = await W32TimeCli.NetServiceAsync("stop", ServiceName, ServiceTimeout, ct).ConfigureAwait(false);
        var start = await W32TimeCli.NetServiceAsync("start", ServiceName, ServiceTimeout, ct).ConfigureAwait(false);

        // 「服务未启动」时 stop 会失败，属于正常情况
        if (start.Ok)
            return TimeSyncActionResult.Success("时间服务已重启", $"net stop → {DescribeResult(stop)}\nnet start → {DescribeResult(start)}");

        if (start.Combined.Contains("已经启动", StringComparison.Ordinal)
            || start.Combined.Contains("already been started", StringComparison.OrdinalIgnoreCase))
            return TimeSyncActionResult.Success("时间服务已在运行", start.Combined.Trim());

        return TimeSyncActionResult.Failure("启动时间服务失败：" + start.FirstLine(), $"net stop → {DescribeResult(stop)}\nnet start → {DescribeResult(start)}");
    }

    /// <summary>把时间服务设为「自动（延迟启动）」并确保在运行。</summary>
    public static async Task<TimeSyncActionResult> EnsureAutoStartAsync(CancellationToken ct = default)
    {
        var start = WriteRegistryDword(W32TimeKey, "Start", 2);
        if (!start.Ok)
            return TimeSyncActionResult.Failure("设为自动启动失败：" + start.Message, start.Detail);
        var delayed = WriteRegistryDword(W32TimeKey, "DelayedAutostart", 1);

        var restart = await RestartServiceAsync(ct).ConfigureAwait(false);
        if (!restart.Ok) return TimeSyncActionResult.Failure("已设为自动启动，但服务启动失败", restart.Detail);

        return TimeSyncActionResult.Success("时间服务已设为自动（延迟启动）并启动",
            delayed.Ok ? restart.Detail : $"{restart.Detail}\n延迟启动写入失败：{delayed.Message}");
    }

    /// <summary>启用 Windows NTP 客户端（注册表 TimeProviders\NtpClient\Enabled）。</summary>
    public static async Task<TimeSyncActionResult> EnableNtpClientAsync(CancellationToken ct = default)
    {
        var enabled = WriteRegistryDword(NtpClientKey, "Enabled", 1);
        if (!enabled.Ok) return enabled;

        var restart = await RestartServiceAsync(ct).ConfigureAwait(false);
        return restart.Ok
            ? TimeSyncActionResult.Success("已启用 Windows NTP 客户端", restart.Detail)
            : TimeSyncActionResult.Failure("已启用 NTP 客户端，但服务重启失败", restart.Detail);
    }

    /// <summary>重新注册 Windows 时间服务（w32tm /register 前先 /unregister，用于配置损坏的情况）。</summary>
    public static async Task<TimeSyncActionResult> ResetTimeServiceAsync(CancellationToken ct = default)
    {
        var steps = new List<string>();

        var unregister = await W32TimeCli.W32TmAsync(ConfigTimeout, "/unregister").ConfigureAwait(false);
        steps.Add($"w32tm /unregister → {DescribeResult(unregister)}");

        var register = await W32TimeCli.W32TmAsync(ConfigTimeout, "/register").ConfigureAwait(false);
        steps.Add($"w32tm /register → {DescribeResult(register)}");
        if (!register.Ok)
            return TimeSyncActionResult.Failure("重新注册时间服务失败", string.Join('\n', steps) + "\n" + register.FirstLine());

        var auto = await EnsureAutoStartAsync(ct).ConfigureAwait(false);
        steps.Add($"设为自动启动 → {DescribeResult(auto)}");

        var client = await EnableNtpClientAsync(ct).ConfigureAwait(false);
        steps.Add($"启用 NTP 客户端 → {DescribeResult(client)}");

        return auto.Ok && client.Ok
            ? TimeSyncActionResult.Success("Windows 时间服务已重置", string.Join('\n', steps))
            : TimeSyncActionResult.Failure("时间服务已重置，但仍有步骤失败", string.Join('\n', steps));
    }

    // ══════════════════ 高级开关 ══════════════════

    /// <summary>
    /// 设置同步频率：>0 时写 SpecialPollInterval（秒）并给时间源加 0x1 标志，0 表示去掉 0x1
    /// 回到系统自适应轮询（此时 SpecialPollInterval 不再生效，故不写注册表）。
    /// </summary>
    public static async Task<TimeSyncActionResult> SetSyncIntervalAsync(int seconds, CancellationToken ct = default)
    {
        if (seconds > 0)
        {
            var write = WriteRegistryDword(NtpClientKey, "SpecialPollInterval", seconds);
            if (!write.Ok) return write;
        }

        var hosts = TimeSyncCatalog.SplitHosts(ReadRegistry().NtpClient.Peers);
        if (hosts.Count == 0)
        {
            var restartOnly = await RestartServiceAsync(ct).ConfigureAwait(false);
            return restartOnly.Ok
                ? TimeSyncActionResult.Success($"同步频率已设为{TimeSyncCatalog.DescribeInterval(seconds)}", restartOnly.Detail)
                : TimeSyncActionResult.Failure("频率已写入，但服务重启失败", restartOnly.Detail);
        }

        return await ApplyServersAsync(hosts, seconds, ct).ConfigureAwait(false);
    }

    /// <summary>开关「SSL 时间种子」（UtilizeSslTimeData）：关闭后可避免个别机器时间被随机跳变。</summary>
    public static async Task<TimeSyncActionResult> SetSslTimeSeedAsync(bool enabled, CancellationToken ct = default)
    {
        var write = WriteRegistryDword(ConfigKey, "UtilizeSslTimeData", enabled ? 1 : 0);
        if (!write.Ok) return write;

        var restart = await RestartServiceAsync(ct).ConfigureAwait(false);
        var text = enabled ? "已启用 SSL 时间种子（系统默认）" : "已关闭 SSL 时间种子";
        return restart.Ok
            ? TimeSyncActionResult.Success(text, restart.Detail)
            : TimeSyncActionResult.Failure($"{text}，但服务重启失败", restart.Detail);
    }

    /// <summary>一键修复：补齐 NTP 客户端、自动启动、服务运行三项，然后按需应用服务器并校时。</summary>
    public static async Task<TimeSyncActionResult> RepairAsync(IReadOnlyList<string>? hosts, int specialPollIntervalSeconds, CancellationToken ct = default)
    {
        var steps = new List<string>();

        var auto = await EnsureAutoStartAsync(ct).ConfigureAwait(false);
        steps.Add($"设为自动启动 → {DescribeResult(auto)}");

        var client = await EnableNtpClientAsync(ct).ConfigureAwait(false);
        steps.Add($"启用 NTP 客户端 → {DescribeResult(client)}");

        if (hosts is { Count: > 0 })
        {
            var apply = await ApplyServersAsync(hosts, specialPollIntervalSeconds, ct).ConfigureAwait(false);
            steps.Add(apply.Ok ? $"应用时间源 → 成功" : $"应用时间源 → 失败：{apply.Message}");
            steps.Add(apply.Detail);

            return apply.Ok
                ? TimeSyncActionResult.Success("修复完成，已应用所选时间源并完成校时", string.Join('\n', steps))
                : TimeSyncActionResult.Failure("修复过程中应用时间源失败", string.Join('\n', steps));
        }

        var restart = await RestartServiceAsync(ct).ConfigureAwait(false);
        steps.Add($"重启时间服务 → {DescribeResult(restart)}");

        var resync = await ResyncAsync(ct).ConfigureAwait(false);
        steps.Add($"立即校时 → {DescribeResult(resync)}");

        return restart.Ok && resync.Ok
            ? TimeSyncActionResult.Success("修复完成，时间服务已就绪", string.Join('\n', steps))
            : TimeSyncActionResult.Failure("修复部分完成，仍有步骤失败", string.Join('\n', steps));
    }

    private static TimeSyncActionResult WriteRegistryDword(string subKey, string name, int value)
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.CreateSubKey(subKey, writable: true);
            if (key is null) return TimeSyncActionResult.Failure("无法打开注册表项", subKey);
            key.SetValue(name, value, RegistryValueKind.DWord);
            return TimeSyncActionResult.Success($"已写入 {name} = {value}");
        }
        catch (UnauthorizedAccessException)
        {
            return TimeSyncActionResult.Failure("拒绝访问：修改系统时间配置需要管理员权限", subKey);
        }
        catch (Exception ex)
        {
            return TimeSyncActionResult.Failure("写入注册表失败", $"{subKey}\\{name}\n{ex.Message}");
        }
    }

    // ══════════════════ 健康检查 / 诊断报告 ══════════════════

    public static IReadOnlyList<TimeSyncIssue> Evaluate(TimeSyncSnapshot snapshot, NtpProbeResult? probe)
    {
        var issues = new List<TimeSyncIssue>();

        if (!snapshot.IsAdmin)
        {
            issues.Add(new TimeSyncIssue("当前未以管理员身份运行",
                "修改时间源、启停时间服务都需要管理员权限。请以管理员身份重新启动空岛图吧工具箱。",
                TimeSyncIssueLevel.Critical));
        }

        if (!snapshot.Service.Exists)
        {
            issues.Add(new TimeSyncIssue("找不到 Windows 时间服务",
                "系统里没有 w32time 服务，时间同步无法工作。可尝试「重置时间服务」重新注册。",
                TimeSyncIssueLevel.Critical));
        }
        else if (snapshot.Service.StartMode == "Disabled")
        {
            issues.Add(new TimeSyncIssue("Windows 时间服务已被禁用",
                "服务被禁用后系统不会自动校时。点「一键修复」会把它改回自动（延迟启动）并启动。",
                TimeSyncIssueLevel.Critical));
        }
        else if (!snapshot.Service.Running)
        {
            issues.Add(new TimeSyncIssue("Windows 时间服务没有运行",
                "服务停止时既不能自动校时，也读不到时间源。点「一键修复」可以直接启动。",
                TimeSyncIssueLevel.Warning));
        }
        else if (snapshot.Service.StartMode == "Manual" && !snapshot.ServiceQueryFailed)
        {
            issues.Add(new TimeSyncIssue("时间服务是手动启动",
                "手动启动的服务在重启后不会自动运行，时间会逐渐漂移；建议改成自动（延迟启动）。",
                TimeSyncIssueLevel.Warning));
        }

        if (!snapshot.NtpClient.Enabled)
        {
            issues.Add(new TimeSyncIssue("Windows NTP 客户端未启用",
                "NTP 客户端关闭时系统只用本机 CMOS 时钟，不会向任何网络时间源校时。",
                TimeSyncIssueLevel.Warning));
        }

        if (snapshot.UsingDomainHierarchy && !snapshot.DomainJoined)
        {
            issues.Add(new TimeSyncIssue("非域机器却配置为域时间层次（NT5DS）",
                "NT5DS 会去找域控校时，非域环境下等于没有时间源。选一个 NTP 服务器应用，或恢复系统默认即可。",
                TimeSyncIssueLevel.Warning));
        }

        if (snapshot.CurrentSource.Length == 0 && snapshot.Service.Running)
        {
            issues.Add(new TimeSyncIssue("读不到当前时间源",
                "系统还没成功同步过任何时间源，选一台服务器点「应用并立即同步」。",
                TimeSyncIssueLevel.Warning));
        }
        else if (snapshot.SourceIsLocalClock)
        {
            issues.Add(new TimeSyncIssue("当前时间源是本机硬件时钟",
                "本机 CMOS 时钟误差会持续累积，网页证书、登录、购票等场景都依赖准确时间。",
                TimeSyncIssueLevel.Warning));
        }

        if (snapshot.Service.Running && snapshot.LastSyncTime is { } last)
        {
            var age = DateTimeOffset.Now - last.ToLocalTime();
            if (age > TimeSpan.FromDays(3))
            {
                issues.Add(new TimeSyncIssue("已经很久没有成功校时",
                    $"上次同步成功是 {last.ToLocalTime():yyyy-MM-dd HH:mm}，距今约 {(int)age.TotalDays} 天。",
                    TimeSyncIssueLevel.Warning));
            }
        }
        else if (snapshot.Service.Running && !snapshot.ServiceQueryFailed)
        {
            issues.Add(new TimeSyncIssue("没有同步成功的记录",
                "系统尚未完成过一次成功校时，通常是时间源不可达（例如默认的 time.windows.com 在部分网络下超时）。",
                TimeSyncIssueLevel.Warning));
        }

        if (probe is { Ok: true })
        {
            var abs = Math.Abs(probe.OffsetMs);
            if (abs > 60_000)
            {
                issues.Add(new TimeSyncIssue("本机时间偏差很大",
                    $"实测与 {probe.Host} 相差 {FormatOffset(probe.OffsetMs)}，已超过 1 分钟，务必立即校时。",
                    TimeSyncIssueLevel.Critical));
            }
            else if (abs > 1000)
            {
                issues.Add(new TimeSyncIssue("本机时间存在偏差",
                    $"实测与 {probe.Host} 相差 {FormatOffset(probe.OffsetMs)}，建议立即校时。",
                    TimeSyncIssueLevel.Warning));
            }
        }
        else if (probe is { Ok: false, Host.Length: > 0 })
        {
            issues.Add(new TimeSyncIssue("时间服务器不可达",
                $"{probe.Host}：{probe.Error}。换个服务器试试，或检查是否屏蔽了 UDP 123。",
                TimeSyncIssueLevel.Warning));
        }

        return issues;
    }

    public static string FormatOffset(double milliseconds)
    {
        var abs = Math.Abs(milliseconds);
        var text = abs >= 1000 ? $"{abs / 1000:F2} 秒" : $"{abs:F0} 毫秒";
        return milliseconds >= 0 ? $"本机慢 {text}" : $"本机快 {text}";
    }

    public static string BuildDiagnosticReport(TimeSyncSnapshot snapshot, IReadOnlyList<NtpProbeResult> probes)
    {
        var text = new System.Text.StringBuilder();
        var now = DateTimeOffset.Now;

        text.AppendLine("=== 空岛图吧工具箱 · 时间同步诊断报告 ===");
        text.AppendLine($"生成时间：{now:yyyy-MM-dd HH:mm:ss}（{TimeZoneInfo.Local.DisplayName}）");
        text.AppendLine($"时区：{TimeZoneInfo.Local.Id}（UTC{FormatUtcOffset(now.Offset)}）");
        text.AppendLine($"管理员权限：{(snapshot.IsAdmin ? "是" : "否")}");
        text.AppendLine($"域成员：{(snapshot.DomainJoined ? "是" : "否")}");
        text.AppendLine();

        text.AppendLine("--- Windows 时间服务 ---");
        text.AppendLine($"服务存在：{(snapshot.Service.Exists ? "是" : "否")}");
        text.AppendLine($"运行状态：{(snapshot.Service.Running ? "运行中" : "已停止")}");
        text.AppendLine($"启动类型：{snapshot.Service.StartModeText}");
        text.AppendLine();

        text.AppendLine("--- NTP 客户端 ---");
        text.AppendLine($"已启用：{(snapshot.NtpClient.Enabled ? "是" : "否")}");
        text.AppendLine($"同步类型：{snapshot.NtpClient.SyncTypeText}");
        text.AppendLine($"配置的时间源：{(snapshot.NtpClient.Peers.Length > 0 ? snapshot.NtpClient.Peers : "（空，使用系统默认）")}");
        text.AppendLine($"固定轮询间隔：{snapshot.NtpClient.SpecialPollInterval} 秒");
        text.AppendLine($"SSL 时间种子：{(snapshot.SslTimeSeedEnabled ? "启用" : "已关闭")}");
        text.AppendLine();

        text.AppendLine("--- 当前同步状态 ---");
        text.AppendLine($"时间源：{(snapshot.CurrentSource.Length > 0 ? snapshot.CurrentSource : "（读不到）")}");
        text.AppendLine($"上次成功同步：{(snapshot.LastSyncTime is { } last ? last.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "（无记录）")}");
        text.AppendLine();

        if (probes.Count > 0)
        {
            text.AppendLine("--- 服务器实测（SNTP / UDP 123）---");
            foreach (var probe in probes)
            {
                text.AppendLine(probe.Ok
                    ? $"{probe.Host}：{probe.RoundTripMs:F0} ms 延迟，层级 {probe.Stratum}，{FormatOffset(probe.OffsetMs)}"
                    : $"{probe.Host}：不可用（{probe.Error}）");
            }
        }
        else
        {
            text.AppendLine("--- 服务器实测 ---");
            text.AppendLine("（本次没有探测记录）");
        }
        text.AppendLine();

        if (!string.IsNullOrWhiteSpace(snapshot.RawStatus))
        {
            text.AppendLine("--- w32tm /query /status ---");
            text.AppendLine(snapshot.RawStatus);
            text.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(snapshot.RawConfiguration))
        {
            text.AppendLine("--- w32tm /query /configuration ---");
            text.AppendLine(snapshot.RawConfiguration);
        }

        return text.ToString();
    }

    private static string FormatUtcOffset(TimeSpan offset)
        => (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm");

    private static string DescribeResult(TimeSyncActionResult result)
        => result.Ok ? "成功" : $"失败（{result.Message}）";

    private static string DescribeResult(CliOutput output)
        => output.Ok ? "成功" : $"失败（{output.FirstLine()}）";

    /// <summary>非管理员时按 UAC 重新启动工具箱（未打包安装模式；MSIX 包无法这样提权）。</summary>
    public static bool TryRestartElevated()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return false;

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
