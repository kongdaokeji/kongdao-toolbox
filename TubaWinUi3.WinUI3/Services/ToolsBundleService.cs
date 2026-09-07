using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public sealed record ToolsBundleUpdateInfo(
    bool HasUpdate,
    string Version,
    string? GitCodeUrl = null,
    string? GitHubUrl = null,
    long Size = 0);

public static class ToolsBundleService
{
    private const string Owner = "luolangaga";
    private const string Repo = "tubatool";
    private const string GitHubReleasesApi = $"https://api.github.com/repos/{Owner}/{Repo}/releases";
    private const string GitCodeOwner = "luolangaga";
    private const string GitCodeRepo = "tubatool";
    private const string GitCodeReleaseApiBase = $"https://api.gitcode.com/api/v5/repos/{GitCodeOwner}/{GitCodeRepo}/releases";
    private const string ToolsAssetName = "Tools.zip";
    private const int ReleasesPerPage = 100;
    private const int MaxReleasePages = 5;

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static string ToolsBundleDir => Path.Combine(
        RuntimeHelper.GetLocalAppDataRoot(),
        "TubaWinUi3", "Tools");

    static ToolsBundleService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolsBundle");
    }

    public static bool IsToolsBundleReady()
    {
        try
        {
            if (!Directory.Exists(ToolsBundleDir)) return false;
            return Directory.EnumerateFileSystemEntries(ToolsBundleDir).Any();
        }
        catch { return false; }
    }

    public static string GetToolsBundleDir() => ToolsBundleDir;

    public static string? GetCurrentVersion()
    {
        return AppSettings.Get("ToolsBundleVersion");
    }

    public static Version? CurrentAppVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is not null ? new Version(v.Major, v.Minor, v.Build) : new Version(1, 0, 0);
        }
    }

    public static async Task<ToolsBundleUpdateInfo?> CheckForToolsUpdateAsync(CancellationToken ct = default)
    {
        var currentVersion = GetCurrentVersion();

        string? gitCodeUrl = null;
        string? githubUrl = null;
        long size = 0;
        string? versionStr = null;

        var gitCodeTask = FetchGitCodeLatestAsync(ct);
        var githubTask = FetchGitHubLatestAsync(ct);

        try
        {
            var gc = await gitCodeTask;
            if (gc is not null)
            {
                gitCodeUrl = gc.Value.Url;
                size = gc.Value.Size;
                versionStr ??= gc.Value.Version;
            }
        }
        catch { }

        try
        {
            var gh = await githubTask;
            if (gh is not null)
            {
                githubUrl = gh.Value.Url;
                size = size > 0 ? size : gh.Value.Size;
                versionStr ??= gh.Value.Version;
            }
        }
        catch { }

        if (versionStr is null) return null;

        if (currentVersion is not null && versionStr == currentVersion)
            return new ToolsBundleUpdateInfo(false, versionStr, gitCodeUrl, githubUrl, size);

        return new ToolsBundleUpdateInfo(true, versionStr, gitCodeUrl, githubUrl, size);
    }

    public static string? PickBestUrl(ToolsBundleUpdateInfo info)
    {
        if (!string.IsNullOrEmpty(info.GitCodeUrl)) return info.GitCodeUrl;
        if (!string.IsNullOrEmpty(info.GitHubUrl)) return info.GitHubUrl;
        return null;
    }

    public static Func<CancellationToken, Task<ResolvedDownloadUrl>> CreateUrlResolver(
        ToolsBundleUpdateInfo info, bool preferGitCode = true)
    {
        return async ct =>
        {
            var url = preferGitCode
                ? (info.GitCodeUrl ?? info.GitHubUrl)
                : (info.GitHubUrl ?? info.GitCodeUrl);

            if (string.IsNullOrEmpty(url))
                throw new InvalidOperationException("没有可用的下载链接");

            var fileName = ToolsAssetName;
            return new ResolvedDownloadUrl(url, fileName, info.Size);
        };
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{(double)bytes / (1L << 30):F2} GB";
        if (bytes >= 1L << 20) return $"{(double)bytes / (1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{(double)bytes / (1L << 10):F1} KB";
        return $"{bytes} B";
    }

    public static string FormatSpeed(double mbps)
    {
        if (mbps >= 1000) return $"{mbps / 1000:F2} Gbps";
        if (mbps >= 1) return $"{mbps:F2} Mbps";
        return $"{mbps * 1000:F0} Kbps";
    }

    public static string FormatTime(TimeSpan? time)
    {
        if (time is null || time.Value.TotalSeconds <= 0) return "--";
        var t = time.Value;
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    private static async Task<(string Url, long Size, string Version)?> FetchGitCodeLatestAsync(CancellationToken ct)
    {
        try
        {
            return await WalkReleasesForToolsAsync(GitCodeReleaseApiBase, ct);
        }
        catch { return null; }
    }

    private static async Task<(string Url, long Size, string Version)?> FetchGitHubLatestAsync(CancellationToken ct)
    {
        try
        {
            return await WalkReleasesForToolsAsync(GitHubReleasesApi, ct);
        }
        catch { return null; }
    }

    /// <summary>
    /// 从最新发行版开始逐版本向下扫描（分页），返回第一个带 Tools.zip 的发行版。
    /// 某个发行版没附带工具包更新时（例如纯应用更新），自动回退到更早的版本。
    /// </summary>
    private static async Task<(string Url, long Size, string Version)?> WalkReleasesForToolsAsync(
        string releasesApi, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolsBundle");

        for (var page = 1; page <= MaxReleasePages; page++)
        {
            var url = $"{releasesApi}?page={page}&per_page={ReleasesPerPage}";
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var match = ScanReleasesForTools(root);
                if (match is not null) return match;

                // 本页不满一页说明已到最后一页，仍未找到 Tools.zip
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < ReleasesPerPage) return null;
            }
            catch { return null; }
        }

        return null;
    }

    /// <summary>
    /// 按 JSON 数组顺序（发行版列表均为最新在前）扫描，返回第一个带 Tools.zip 的发行版。
    /// </summary>
    internal static (string Url, long Size, string Version)? ScanReleasesForTools(JsonElement releases)
    {
        if (releases.ValueKind != JsonValueKind.Array) return null;

        foreach (var release in releases.EnumerateArray())
        {
            var match = ParseToolsAsset(release);
            if (match is not null) return match;
        }

        return null;
    }

    private static (string Url, long Size, string Version)? ParseToolsAsset(JsonElement release)
    {
        var tagName = release.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
        if (tagName.Length == 0) return null;

        // 与 /releases/latest 语义一致：跳过草稿和预发布
        if (release.TryGetProperty("draft", out var draftEl) && draftEl.GetBoolean()) return null;
        if (release.TryGetProperty("prerelease", out var preEl) && preEl.GetBoolean()) return null;

        if (!release.TryGetProperty("assets", out var assetsEl)) return null;

        foreach (var asset in assetsEl.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.Equals(ToolsAssetName, StringComparison.OrdinalIgnoreCase)) continue;

            var downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (string.IsNullOrEmpty(downloadUrl)) continue;

            var versionStr = tagName.TrimStart('v', 'V');
            var assetSize = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;
            return (downloadUrl, assetSize, versionStr);
        }

        return null;
    }
}
