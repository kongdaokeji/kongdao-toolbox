using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services;

public sealed class UupBuildInfo
{
    public string UpdateId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Build { get; set; } = "";
    public DateTime DateAdded { get; set; }
    public string Category { get; set; } = "";
}

public sealed class UupLanguageInfo
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class UupEditionInfo
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsBaseEdition { get; set; }
    public List<string> RequiredBaseEditions { get; set; } = [];
}

public sealed class UupDownloadInfo
{
    public string UpdateId { get; set; } = "";
    public string Language { get; set; } = "";
    public List<string> Editions { get; set; } = [];
    public int AutoDl { get; set; } = 2;
    public List<string> VirtualEditions { get; set; } = [];
}

public sealed class UupQuickFetchOption
{
    public string DisplayName { get; set; } = "";
    public string Ring { get; set; } = "";
    public string Arch { get; set; } = "";
}

public sealed class UupNewBuildRequest
{
    public string Arch { get; set; } = "amd64";
    public string Ring { get; set; } = "WIF";
    public string Flight { get; set; } = "Mainline";
    public string Build { get; set; } = "";
    public int Minor { get; set; }
    public int Sku { get; set; } = 48;
}

public static class UupDumpService
{
    private const string BaseUrl = "https://uupdump.net";
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
        DefaultRequestHeaders =
        {
            { "User-Agent", "TubaWinUi3-UupDump/1.0" }
        }
    };

    private static readonly Dictionary<string, string> LanguageNames = new()
    {
        ["ar-sa"] = "阿拉伯语", ["bg-bg"] = "保加利亚语", ["zh-cn"] = "简体中文",
        ["zh-tw"] = "繁体中文", ["hr-hr"] = "克罗地亚语", ["cs-cz"] = "捷克语",
        ["da-dk"] = "丹麦语", ["nl-nl"] = "荷兰语", ["en-gb"] = "英语(英)",
        ["en-us"] = "英语(美)", ["et-ee"] = "爱沙尼亚语", ["fi-fi"] = "芬兰语",
        ["fr-ca"] = "法语(加)", ["fr-fr"] = "法语", ["de-de"] = "德语",
        ["el-gr"] = "希腊语", ["he-il"] = "希伯来语", ["hu-hu"] = "匈牙利语",
        ["it-it"] = "意大利语", ["ja-jp"] = "日语", ["ko-kr"] = "韩语",
        ["lv-lv"] = "拉脱维亚语", ["lt-lt"] = "立陶宛语", ["nb-no"] = "挪威语",
        ["pl-pl"] = "波兰语", ["pt-br"] = "葡萄牙语(巴)", ["pt-pt"] = "葡萄牙语",
        ["ro-ro"] = "罗马尼亚语", ["ru-ru"] = "俄语", ["sr-latn-rs"] = "塞尔维亚语",
        ["sk-sk"] = "斯洛伐克语", ["sl-si"] = "斯洛文尼亚语", ["es-mx"] = "西班牙语(墨)",
        ["es-es"] = "西班牙语", ["sv-se"] = "瑞典语", ["th-th"] = "泰语",
        ["tr-tr"] = "土耳其语", ["uk-ua"] = "乌克兰语", ["neutral"] = "任意语言"
    };

    private static readonly Dictionary<string, string> EditionNames = new()
    {
        ["CORE"] = "Windows 家庭版",
        ["COREN"] = "Windows 家庭版 N",
        ["CORESINGLELANGUAGE"] = "Windows 家庭单语言版",
        ["CORECOUNTRYSPECIFIC"] = "Windows 家庭中文版",
        ["PROFESSIONAL"] = "Windows 专业版",
        ["PROFESSIONALN"] = "Windows 专业版 N",
        ["PROFESSIONALWORKSTATION"] = "Windows 专业工作站版",
        ["PROFESSIONALWORKSTATIONN"] = "Windows 专业工作站版 N",
        ["PROFESSIONALEDUCATION"] = "Windows 专业教育版",
        ["PROFESSIONALEDUCATIONN"] = "Windows 专业教育版 N",
        ["EDUCATION"] = "Windows 教育版",
        ["EDUCATIONN"] = "Windows 教育版 N",
        ["ENTERPRISE"] = "Windows 企业版",
        ["ENTERPRISEN"] = "Windows 企业版 N",
        ["ENTERPRISEG"] = "Windows 企业版 G",
        ["ENTERPRISEGN"] = "Windows 企业版 G N",
        ["ENTERPRISES"] = "Windows 企业版 S",
        ["ENTERPRISESN"] = "Windows 企业版 S N",
        ["SERVERRDSH"] = "Windows 企业多会话版",
        ["IOTENTERPRISE"] = "Windows IoT 企业版",
        ["IOTENTERPRISEK"] = "Windows IoT 企业版订阅",
        ["IOTENTERPRISES"] = "Windows IoT 企业版 S",
        ["IOTENTERPRISESK"] = "Windows IoT 企业版 S 订阅",
        ["CLOUD"] = "Windows Cloud",
        ["CLOUDN"] = "Windows Cloud N",
        ["CLOUDE"] = "Windows Cloud Edition",
        ["CLOUDEN"] = "Windows Cloud Edition N",
        ["SERVERSTANDARD"] = "Windows Server 标准版",
        ["SERVERSTANDARDCORE"] = "Windows Server 标准版 (Core)",
        ["SERVERDATACENTER"] = "Windows Server 数据中心版",
        ["SERVERDATACENTERCORE"] = "Windows Server 数据中心版 (Core)",
        ["SERVERTURBINE"] = "Windows Server Turbine",
        ["SERVERTURBINECORE"] = "Windows Server Turbine (Core)",
        ["SERVERAZURESTACKHCICOR"] = "Windows Server Azure Stack HCI (Core)",
        ["PPIPRO"] = "Windows Team",
        ["STARTER"] = "Windows 入门版",
        ["STARTERN"] = "Windows 入门版 N"
    };

    private static readonly Dictionary<string, string> VirtualEditionNames = new()
    {
        ["PROFESSIONALWORKSTATION"] = "专业工作站版",
        ["PROFESSIONALEDUCATION"] = "专业教育版",
        ["EDUCATION"] = "教育版",
        ["ENTERPRISE"] = "企业版",
        ["SERVERRDSH"] = "企业多会话版",
        ["IOTENTERPRISE"] = "IoT 企业版",
        ["IOTENTERPRISEK"] = "IoT 企业版订阅",
        ["IOTENTERPRISES"] = "IoT 企业版 S",
        ["IOTENTERPRISESK"] = "IoT 企业版 S 订阅"
    };

    public static IReadOnlyDictionary<string, string> GetLanguageNames() => LanguageNames;
    public static IReadOnlyDictionary<string, string> GetEditionNames() => EditionNames;
    public static IReadOnlyDictionary<string, string> GetVirtualEditionNames() => VirtualEditionNames;

    public static string GetEditionDisplayName(string editionId) =>
        EditionNames.TryGetValue(editionId, out var name) ? name : editionId;

    public static string GetLanguageDisplayName(string code) =>
        LanguageNames.TryGetValue(code, out var name) ? name : code;

    public static List<UupQuickFetchOption> GetQuickFetchOptions() =>
    [
        new() { DisplayName = "最新正式版 (x64)", Ring = "Retail", Arch = "amd64" },
        new() { DisplayName = "最新正式版 (ARM64)", Ring = "Retail", Arch = "arm64" },
        new() { DisplayName = "最新预览版 (x64)", Ring = "RP", Arch = "amd64" },
        new() { DisplayName = "最新预览版 (ARM64)", Ring = "RP", Arch = "arm64" },
        new() { DisplayName = "最新 Beta 版 (x64)", Ring = "WIS", Arch = "amd64" },
        new() { DisplayName = "最新 Beta 版 (ARM64)", Ring = "WIS", Arch = "arm64" },
        new() { DisplayName = "最新 Dev 版 (x64)", Ring = "WIF", Arch = "amd64" },
        new() { DisplayName = "最新 Dev 版 (ARM64)", Ring = "WIF", Arch = "arm64" },
        new() { DisplayName = "最新 Canary 版 (x64)", Ring = "Canary", Arch = "amd64" },
        new() { DisplayName = "最新 Canary 版 (ARM64)", Ring = "Canary", Arch = "arm64" },
    ];

    public static async Task<List<UupBuildInfo>> FetchLatestBuildsAsync(string ring, string arch, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/fetchupd.php?arch={arch}&ring={ring}&flight=Mainline&build=26100.1";
        var html = await _http.GetStringAsync(url, ct);
        return ParseBuildListHtml(html);
    }

    public static async Task<List<UupBuildInfo>> FetchNewBuildAsync(UupNewBuildRequest req, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/fetchupd.php?arch={req.Arch}&ring={req.Ring}&flight={req.Flight}&build={req.Build}&minor={req.Minor}&sku={req.Sku}";
        var html = await _http.GetStringAsync(url, ct);
        return ParseBuildListHtml(html);
    }

    public static async Task<List<UupBuildInfo>> GetKnownBuildsAsync(string? search = null, string? category = null, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/known.php";
        if (!string.IsNullOrEmpty(search))
            url += $"?q={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrEmpty(category))
        {
            var sep = url.Contains('?') ? "&" : "?";
            url += $"{sep}q=category:{Uri.EscapeDataString(category)}";
        }

        var html = await _http.GetStringAsync(url, ct);
        return ParseKnownBuildsHtml(html);
    }

    public static async Task<List<UupLanguageInfo>> GetLanguagesAsync(string updateId, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/selectlang.php?id={Uri.EscapeDataString(updateId)}";
        var html = await _http.GetStringAsync(url, ct);
        return ParseLanguagesHtml(html);
    }

    public static async Task<List<UupEditionInfo>> GetEditionsAsync(string updateId, string language, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/selectedition.php?id={Uri.EscapeDataString(updateId)}&pack={Uri.EscapeDataString(language)}";
        var html = await _http.GetStringAsync(url, ct);
        return ParseEditionsHtml(html);
    }

    public static string BuildGetUrl(UupDownloadInfo info)
    {
        var editions = string.Join("&edition=", info.Editions.Select(Uri.EscapeDataString));
        return $"{BaseUrl}/get.php?id={Uri.EscapeDataString(info.UpdateId)}&pack={Uri.EscapeDataString(info.Language)}&edition={editions}&autodl={info.AutoDl}";
    }

    public static async Task<string> DownloadPackageAsync(UupDownloadInfo info, string destDir, IProgress<(int percent, string status)>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);

        if (info.AutoDl == 3 && info.VirtualEditions.Count > 0)
            return await DownloadPackagePostAsync(info, destDir, progress, ct);

        return await DownloadPackageGetAsync(info, destDir, progress, ct);
    }

    private static async Task<string> DownloadPackageGetAsync(UupDownloadInfo info, string destDir, IProgress<(int percent, string status)>? progress, CancellationToken ct)
    {
        var url = BuildGetUrl(info);
        return await StreamZipToFileAsync(url, destDir, progress, ct);
    }

    private static async Task<string> DownloadPackagePostAsync(UupDownloadInfo info, string destDir, IProgress<(int percent, string status)>? progress, CancellationToken ct)
    {
        var getUrl = BuildGetUrl(info);
        var content = new FormUrlEncodedContent(
            info.VirtualEditions.Select(ve => new KeyValuePair<string, string>("virtualEditions[]", ve)));

        var request = new HttpRequestMessage(HttpMethod.Post, getUrl) { Content = content };

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var ctHeader = response.Content.Headers.ContentType;
        if (ctHeader is not null && ctHeader.MediaType == "application/zip")
            return await StreamResponseToFileAsync(response, destDir, progress, ct);

        var html = await response.Content.ReadAsStringAsync(ct);
        var redirectUrl = ExtractRedirectFromHtml(html);
        if (!string.IsNullOrEmpty(redirectUrl))
            return await StreamZipToFileAsync(redirectUrl, destDir, progress, ct);

        throw new InvalidOperationException("服务器未返回 ZIP 文件。请尝试在浏览器中手动下载。");
    }

    private static async Task<string> StreamZipToFileAsync(string url, string destDir, IProgress<(int percent, string status)>? progress, CancellationToken ct)
    {
        progress?.Report((0, "正在连接服务器..."));

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        return await StreamResponseToFileAsync(response, destDir, progress, ct);
    }

    private static async Task<string> StreamResponseToFileAsync(HttpResponseMessage response, string destDir, IProgress<(int percent, string status)>? progress, CancellationToken ct)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var fileName = "uup_dlp.zip";
        if (disposition is not null)
        {
            var fn = disposition.FileName?.Trim('"');
            if (!string.IsNullOrEmpty(fn)) fileName = fn;
        }

        var destPath = Path.Combine(destDir, fileName);
        var totalBytes = response.Content.Headers.ContentLength ?? 0;

        progress?.Report((5, "正在下载 UUP 转换包..."));

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var fs = File.Create(destPath);

        var buffer = new byte[81920];
        long bytesRead = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
            bytesRead += read;
            if (totalBytes > 0)
            {
                var pct = Math.Min((int)(bytesRead * 100 / totalBytes), 99);
                progress?.Report((pct, $"正在下载转换包... {pct}%"));
            }
        }

        progress?.Report((100, "下载完成"));
        return destPath;
    }

    private static string ExtractRedirectFromHtml(string html)
    {
        var metaMatch = Regex.Match(html, @"<meta[^>]*http-equiv=""refresh""[^>]*content=""[^;]*;url=([^""]+)""", RegexOptions.IgnoreCase);
        if (metaMatch.Success) return metaMatch.Groups[1].Value;

        var locMatch = Regex.Match(html, @"window\.location\s*=\s*'([^']+)'", RegexOptions.IgnoreCase);
        if (locMatch.Success) return locMatch.Groups[1].Value;

        var linkMatch = Regex.Match(html, @"href=""(get\.php[^""]*autodl=3[^""]*)""", RegexOptions.IgnoreCase);
        if (linkMatch.Success)
        {
            var u = linkMatch.Groups[1].Value;
            return u.StartsWith("http") ? u : $"{BaseUrl}/{u}";
        }

        var pkgMatch = Regex.Match(html, @"href=""([^""]*pack_[^""]*\.zip)""", RegexOptions.IgnoreCase);
        if (pkgMatch.Success)
        {
            var u = pkgMatch.Groups[1].Value;
            return u.StartsWith("http") ? u : $"{BaseUrl}/{u}";
        }

        return "";
    }

    public static string GetDownloadDir()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "UUPDump");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static List<UupBuildInfo> ParseBuildListHtml(string html)
    {
        var builds = new List<UupBuildInfo>();
        var rows = Regex.Matches(html, @"<a[^>]*href=""selectlang\.php\?id=([a-f0-9\-]+)""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match m in rows)
        {
            var id = m.Groups[1].Value;
            var title = StripTags(m.Groups[2].Value).Trim();
            if (string.IsNullOrEmpty(title)) continue;

            var arch = "amd64";
            if (title.Contains("arm64", StringComparison.OrdinalIgnoreCase)) arch = "arm64";
            else if (title.Contains("x86", StringComparison.OrdinalIgnoreCase)) arch = "x86";

            var channel = "";
            if (title.Contains("Canary", StringComparison.OrdinalIgnoreCase)) channel = "Canary";
            else if (title.Contains("Dev", StringComparison.OrdinalIgnoreCase)) channel = "Dev";
            else if (title.Contains("Beta", StringComparison.OrdinalIgnoreCase)) channel = "Beta";
            else if (title.Contains("Release Preview", StringComparison.OrdinalIgnoreCase) || title.Contains("RP", StringComparison.OrdinalIgnoreCase)) channel = "Release Preview";
            else if (title.Contains("Retail", StringComparison.OrdinalIgnoreCase)) channel = "Retail";

            var buildMatch = Regex.Match(title, @"(\d+\.\d+)");
            var build = buildMatch.Success ? buildMatch.Groups[1].Value : "";

            builds.Add(new UupBuildInfo
            {
                UpdateId = id,
                Title = title,
                Architecture = arch,
                Channel = channel,
                Build = build,
                Category = DetermineCategory(title)
            });
        }

        return builds;
    }

    private static List<UupBuildInfo> ParseKnownBuildsHtml(string html)
    {
        var builds = new List<UupBuildInfo>();
        var rows = Regex.Matches(html, @"<a[^>]*href=""selectlang\.php\?id=([a-f0-9\-]+)""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match m in rows)
        {
            var id = m.Groups[1].Value;
            var title = StripTags(m.Groups[2].Value).Trim();
            if (string.IsNullOrEmpty(title)) continue;

            var arch = "amd64";
            if (title.Contains("arm64", StringComparison.OrdinalIgnoreCase)) arch = "arm64";
            else if (title.Contains("x86", StringComparison.OrdinalIgnoreCase)) arch = "x86";

            var channel = "";
            if (title.Contains("Canary", StringComparison.OrdinalIgnoreCase)) channel = "Canary";
            else if (title.Contains("Dev", StringComparison.OrdinalIgnoreCase)) channel = "Dev";
            else if (title.Contains("Beta", StringComparison.OrdinalIgnoreCase)) channel = "Beta";
            else if (title.Contains("Release Preview", StringComparison.OrdinalIgnoreCase)) channel = "Release Preview";

            var buildMatch = Regex.Match(title, @"(\d+\.\d+)");
            var build = buildMatch.Success ? buildMatch.Groups[1].Value : "";

            builds.Add(new UupBuildInfo
            {
                UpdateId = id,
                Title = title,
                Architecture = arch,
                Channel = channel,
                Build = build,
                Category = DetermineCategory(title)
            });
        }

        return builds;
    }

    private static List<UupLanguageInfo> ParseLanguagesHtml(string html)
    {
        var langs = new List<UupLanguageInfo>();

        var matches = Regex.Matches(html, @"href=""selectedition\.php\?id=[^""]*&pack=([a-z]{2}-[a-z]{2}|neutral)""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match m in matches)
        {
            var code = m.Groups[1].Value;
            var name = StripTags(m.Groups[2].Value).Trim();
            var displayName = LanguageNames.TryGetValue(code, out var dn) ? $"{dn} ({code})" : name;
            if (string.IsNullOrEmpty(displayName)) displayName = code;
            langs.Add(new UupLanguageInfo { Code = code, DisplayName = displayName });
        }

        if (langs.Count == 0)
        {
            var optMatches = Regex.Matches(html, @"<option[^>]*value=""([a-z]{2}-[a-z]{2}|neutral)""[^>]*>(.*?)</option>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in optMatches)
            {
                var code = m.Groups[1].Value;
                var name = StripTags(m.Groups[2].Value).Trim();
                var displayName = LanguageNames.TryGetValue(code, out var dn) ? $"{dn} ({code})" : name;
                if (string.IsNullOrEmpty(displayName)) displayName = code;
                langs.Add(new UupLanguageInfo { Code = code, DisplayName = displayName });
            }
        }

        return langs.DistinctBy(l => l.Code).ToList();
    }

    private static List<UupEditionInfo> ParseEditionsHtml(string html)
    {
        var editions = new List<UupEditionInfo>();

        foreach (var pattern in new[]
        {
            @"name=""edition\[\]""[^>]*value=""([^""]+)""",
            @"name=""edition[]""[^>]*value=""([^""]+)""",
            @"<input[^>]*name=""edition[]""[^>]*value=""([^""]+)""",
            @"<option[^>]*value=""([^""]+)""[^>]*>(?:All editions|全部版本)"
        })
        {
            var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                var id = m.Groups[1].Value;
                if (editions.Any(e => e.Id == id)) continue;
                editions.Add(new UupEditionInfo
                {
                    Id = id,
                    DisplayName = GetEditionDisplayName(id),
                    IsBaseEdition = true
                });
            }
            if (editions.Count > 0) break;
        }

        var allEditionsMatch = Regex.Match(html, @"<option[^>]*value=""0""[^>]*>", RegexOptions.IgnoreCase);
        if (allEditionsMatch.Success && editions.Count == 0)
        {
            editions.Add(new UupEditionInfo { Id = "0", DisplayName = "所有版本", IsBaseEdition = true });
        }

        foreach (var pattern in new[]
        {
            @"name=""virtEdition\[\]""[^>]*value=""([^""]+)""",
            @"name=""virtualEditions\[\]""[^>]*value=""([^""]+)""",
            @"name=""virtEdition[]""[^>]*value=""([^""]+)"""
        })
        {
            var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                var id = m.Groups[1].Value;
                if (editions.Any(e => e.Id == id)) continue;
                var vName = VirtualEditionNames.TryGetValue(id, out var vn) ? vn : id;
                editions.Add(new UupEditionInfo
                {
                    Id = id,
                    DisplayName = $"[虚拟] {vName}",
                    IsBaseEdition = false
                });
            }
        }

        return editions.DistinctBy(e => e.Id).ToList();
    }

    private static string DetermineCategory(string title)
    {
        var t = title.ToLowerInvariant();
        if (t.Contains("server")) return "Windows Server";
        if (t.Contains("canary")) return "Canary";
        if (t.Contains("2610") || t.Contains("2620") || t.Contains("2630") || t.Contains("26100") || t.Contains("26200") || t.Contains("26300")) return "Windows 11 24H2+";
        if (t.Contains("22631")) return "Windows 11 23H2";
        if (t.Contains("22621")) return "Windows 11 22H2";
        if (t.Contains("22000")) return "Windows 11 21H2";
        if (t.Contains("19045") || t.Contains("19044") || t.Contains("19043")) return "Windows 10 22H2";
        if (t.Contains("1904")) return "Windows 10";
        return "其他";
    }

    private static string StripTags(string html) => Regex.Replace(html, "<[^>]+>", "").Trim();
}
