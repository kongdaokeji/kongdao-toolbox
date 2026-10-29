using System.Text.Json;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services;

/// <summary>一台/一组预置 NTP 时间源。Hosts 会按「主 + 备用」写成 manualpeerlist（多个对等端更抗单点抖动）。</summary>
public sealed record NtpPreset(string Id, string Name, string Summary, string[] Hosts, string Note);

/// <summary>时间同步页的用户选择（只用于回显上次用过什么，系统真实配置一律以注册表为准）。</summary>
public sealed class TimeSyncSettings
{
    /// <summary>上次应用的预设 Id；空表示未选（保持系统现状）。</summary>
    public string PresetId { get; set; } = "";
    /// <summary>上次使用的自定义地址原文（可含多个，逗号/空格分隔）。</summary>
    public string CustomHosts { get; set; } = "";
    /// <summary>固定同步间隔（秒）；0 = 用系统自适应轮询间隔（默认，推荐）。</summary>
    public int SyncIntervalSeconds { get; set; }
    /// <summary>是否已关闭「SSL 时间种子」（UtilizeSslTimeData=0）。</summary>
    public bool SslTimeSeedDisabled { get; set; }
}

/// <summary>内置 NTP 预设清单 + 设置存储 + 对等列表拼装（全部为纯逻辑，便于单测）。</summary>
public static class TimeSyncCatalog
{
    /// <summary>NTP 客户端模式标志（官方 NtpServer 标志位：0x1 SpecialInterval / 0x2 UseAsFallbackOnly / 0x4 SymmetricActive / 0x8 Client）。</summary>
    public const int ClientFlag = 0x8;
    public const int SpecialIntervalFlag = 0x1;

    /// <summary>同步间隔可选项（秒）；0 = 系统自适应。</summary>
    public static readonly (int Seconds, string Label)[] IntervalOptions =
    [
        (0, "系统自适应（推荐）"),
        (3600, "每小时"),
        (21600, "每 6 小时"),
        (43200, "每 12 小时"),
        (86400, "每天"),
    ];

    private static readonly NtpPreset[] _presets =
    [
        new("aliyun", "阿里云 NTP",
            "国内最常用的公共时间源，延迟低、稳定",
            ["ntp.aliyun.com", "ntp1.aliyun.com", "ntp2.aliyun.com", "ntp3.aliyun.com"],
            "阿里云官方公共 NTP 服务，国内线路优先，日常首选。"),
        new("tencent", "腾讯云 NTP",
            "国内线路稳定，与阿里云互为备份",
            ["ntp.tencent.com", "ntp1.tencent.com", "ntp2.tencent.com", "ntp3.tencent.com"],
            "腾讯云官方公共 NTP 服务，电信/联通/移动线路覆盖好。"),
        new("ntsc", "国家授时中心",
            "中科院国家授时中心，权威授时源",
            ["ntp.ntsc.ac.cn"],
            "地址本身是官方地址（ntp.ntsc.ac.cn），能不能连上取决于所在网络——部分网络会屏蔽公网 UDP 123，点「测速」可直接验证。"),
        new("sjtu", "上海交大 NTP",
            "教育网一级时间源（stratum 1）",
            ["ntp.sjtu.edu.cn"],
            "校园网 / 教育网用户延迟最低；公网访问可能有速率限制。"),
        new("cloudflare", "Cloudflare NTP",
            "全球任播，国际线路友好",
            ["time.cloudflare.com"],
            "海外线路或跨境网络下表现更好，国内延迟相对高一些。"),
        new("apple", "苹果 NTP",
            "时间服务器池，国际线路",
            ["time.apple.com"],
            "苹果公共时间源，国内可直连，稳定性取决于出口线路。"),
        new("pool", "NTP Pool（中国区）",
            "社区服务器池，随机分配节点",
            ["cn.pool.ntp.org"],
            "pool.ntp.org 中国区池，节点质量参差，延迟波动比厂商时间源大。"),
        new("microsoft", "Windows 默认",
            "系统出厂默认时间源",
            ["time.windows.com"],
            "Windows 出厂默认值（time.windows.com，标志 0x9）。国内线路偶发超时、响应慢，正是「网络正常但时间不对」的常见来源，建议先测速再决定是否继续使用。"),
    ];

    public static IReadOnlyList<NtpPreset> Presets => _presets;

    public static NtpPreset? FindPreset(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : _presets.FirstOrDefault(p => p.Id == id);

    /// <summary>把注册表里的 NtpServer 字符串（如 "ntp.aliyun.com,0x8 ntp1.aliyun.com,0x8"）反查回预设。</summary>
    public static NtpPreset? MatchPreset(string? registryPeers)
    {
        var first = FirstPeer(registryPeers);
        if (first is null) return null;
        return _presets.FirstOrDefault(p => p.Hosts.Any(h => string.Equals(h, first, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>取对等列表里的第一台主机（去掉 ",0x8" 之类的标志）。</summary>
    public static string? FirstPeer(string? registryPeers)
    {
        if (string.IsNullOrWhiteSpace(registryPeers)) return null;
        foreach (var entry in registryPeers.Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var host = StripFlags(entry);
            if (host.Length > 0 && !IsFlagLike(host)) return host;
        }
        return null;
    }

    /// <summary>统计注册表对等列表里的主机数量。</summary>
    public static int CountPeers(string? registryPeers)
        => string.IsNullOrWhiteSpace(registryPeers)
            ? 0
            : registryPeers
                .Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(StripFlags)
                .Count(h => h.Length > 0 && !IsFlagLike(h));

    /// <summary>
    /// 拼装 w32tm /config 的 manualpeerlist：每台服务器带客户端模式标志（0x8），
    /// 固定间隔开启时再加 0x1（SpecialInterval，官方标志位）。
    /// </summary>
    public static string BuildPeerList(IEnumerable<string> hosts, bool useSpecialInterval = false)
    {
        var flag = useSpecialInterval ? $"0x{ClientFlag | SpecialIntervalFlag:X}" : $"0x{ClientFlag:X}";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();

        foreach (var raw in hosts)
        {
            var host = StripFlags(raw?.Trim() ?? "");
            if (host.Length == 0 || !seen.Add(host)) continue;
            parts.Add($"{host},{flag}");
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// 把用户输入或注册表值拆成主机名列表（支持逗号、分号、空格以及中文标点）。
    /// 顺带处理注册表形态：「host,0x8」里的标志部分会被丢弃，不会被当成一台服务器。
    /// </summary>
    public static List<string> SplitHosts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw
            .Split([',', '，', ';', '；', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => StripFlags(s.Trim()))
            .Where(s => s.Length > 0 && !IsFlagLike(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>校验单个主机名/地址，返回中文错误说明；合法时返回 null。</summary>
    public static string? ValidateHost(string? host)
    {
        var value = StripFlags(host?.Trim() ?? "");
        if (value.Length == 0) return LocalizationService.L("TimeSync_ErrAddressEmpty", "地址不能为空");
        if (value.Length > 253) return LocalizationService.L("TimeSync_ErrAddressTooLong", "地址过长");
        if (value.Contains("://", StringComparison.Ordinal)) return LocalizationService.L("TimeSync_ErrAddressScheme", "只填服务器地址，不要带 http:// 之类的前缀");
        if (value.Contains('/') || value.Contains('\\')) return LocalizationService.L("TimeSync_ErrAddressSlash", "地址里不能有斜杠");
        if (value.Contains(':')) return LocalizationService.L("TimeSync_ErrAddressPort", "主机名不要带端口，NTP 固定使用 UDP 123");
        if (!Regex.IsMatch(value, @"^[A-Za-z0-9._-]+$")) return LocalizationService.L("TimeSync_ErrAddressChars", "地址只能包含字母、数字、点、连字符和下划线");
        if (value.StartsWith('.') || value.EndsWith('.') || value.Contains("..")) return LocalizationService.L("TimeSync_ErrDomainFormat", "域名格式不正确");
        if (value.Replace(".", "").Length == 0) return LocalizationService.L("TimeSync_ErrDomainFormat", "域名格式不正确");
        return null;
    }

    /// <summary>批量校验，返回第一个错误说明；全部合法时返回 null。</summary>
    public static string? ValidateHosts(IEnumerable<string> hosts)
    {
        var list = hosts.Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
        if (list.Count == 0) return LocalizationService.L("TimeSync_ErrNoServers", "至少填写一台 NTP 服务器地址");
        foreach (var host in list)
        {
            var error = ValidateHost(host);
            if (error is not null) return $"{host.Trim()}：{error}";
        }
        return null;
    }

    /// <summary>去掉 ",0x8" 这类标志后缀，只留下主机名。</summary>
    public static string StripFlags(string entry)
    {
        var trimmed = entry.Trim();
        var comma = trimmed.IndexOf(',');
        return comma >= 0 ? trimmed[..comma].Trim() : trimmed;
    }

    private static bool IsFlagLike(string value)
        => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || value.All(char.IsDigit);

    public static string DescribeInterval(int seconds)
    {
        var match = IntervalOptions.FirstOrDefault(o => o.Seconds == seconds);
        return match.Seconds == seconds && match.Label is not null
            ? LocalizationService.L($"TimeSync_Interval_{seconds}", match.Label)
            : string.Format(LocalizationService.L("TimeSync_SecondsFormat", "{0} 秒"), seconds);
    }

    #region 设置存储

    /// <summary>测试用数据目录覆盖（与 GameTunnelCatalog 同一套约定）。</summary>
    public static string? DataDirOverride { get; set; }

    public static string DataDir
    {
        get
        {
            var dir = DataDirOverride ?? Path.Combine(ConfigManager.GetDataDir(), "TimeSync");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string SettingsPath => Path.Combine(DataDir, "settings.json");

    public static TimeSyncSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<TimeSyncSettings>(File.ReadAllText(SettingsPath)) ?? new TimeSyncSettings();
        }
        catch
        {
            // 配置损坏时退回默认值，不影响本次操作
        }
        return new TimeSyncSettings();
    }

    public static void SaveSettings(TimeSyncSettings settings)
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    #endregion
}
