using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services;

public sealed class MicrosoftEdition
{
    public string Name { get; init; } = "";
    public int[] SkuIds { get; init; } = [];
    public string Product { get; init; } = "";
    public string Arch { get; init; } = "x64";
}

public sealed class MicrosoftLanguage
{
    public string Name { get; init; } = "";
    public int SkuId { get; init; }
    public string SessionId { get; init; } = "";
}

public static class MicrosoftOfficialService
{
    private const string ProfileId = "606624d44113";
    private const string OrgId = "y6jn8c31";
    private const string InstanceId = "560dc9f3-1aa5-4a2f-b63c-9e18f8d0e175";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static MicrosoftOfficialService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    }

    public static List<MicrosoftEdition> GetAvailableEditions()
    {
        return
        [
            new() { Name = "Windows 11 24H2 (家庭/专业版)", SkuIds = [3321, 3324], Product = "windows11", Arch = "x64" },
            new() { Name = "Windows 11 24H2 (家庭/专业版 ARM64)", SkuIds = [3324], Product = "windows11arm64", Arch = "ARM64" },
            new() { Name = "Windows 11 24H2 家庭中国版", SkuIds = [3322], Product = "windows11", Arch = "x64" },
            new() { Name = "Windows 11 24H2 专业中国版", SkuIds = [3323], Product = "windows11", Arch = "x64" },
            new() { Name = "Windows 11 23H2 (家庭/专业版)", SkuIds = [2361, 2379], Product = "windows11", Arch = "x64" },
            new() { Name = "Windows 10 22H2 (家庭/专业版)", SkuIds = [2618], Product = "Windows10ISO", Arch = "x64" },
            new() { Name = "Windows 10 22H2 (家庭/专业版 ARM64)", SkuIds = [2619], Product = "Windows10ISO", Arch = "ARM64" },
            new() { Name = "Windows 10 家庭中国版", SkuIds = [2378], Product = "Windows10ISO", Arch = "x64" },
        ];
    }

    public static async Task<string> InitSessionAsync(CancellationToken ct = default)
    {
        var sessionId = Guid.NewGuid().ToString();

        try
        {
            var tagUrl = $"https://vlscppe.microsoft.com/tags?org_id={OrgId}&session_id={sessionId}";
            await _http.GetStringAsync(tagUrl, ct);
        }
        catch { }

        try
        {
            var mdtUrl = $"https://ov-df.microsoft.com/mdt.js?instanceId={InstanceId}&PageId=si&session_id={sessionId}";
            var mdtResp = await _http.GetStringAsync(mdtUrl, ct);

            var wMatch = Regex.Match(mdtResp, @"[?&]w=([A-Fa-f0-9]+)");
            var rticksMatch = Regex.Match(mdtResp, @"rticks\=""?\+?(\d+)");

            if (wMatch.Success && rticksMatch.Success)
            {
                var w = wMatch.Groups[1].Value;
                var rticks = rticksMatch.Groups[1].Value;
                var epoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var replyUrl = $"https://ov-df.microsoft.com/?session_id={sessionId}&CustomerId={InstanceId}&PageId=si&w={w}&mdt={epoch}&rticks={rticks}";
                await _http.GetStringAsync(replyUrl, ct);
            }
        }
        catch { }

        return sessionId;
    }

    public static async Task<List<MicrosoftLanguage>> GetLanguagesAsync(
        int productEditionId,
        string sessionId,
        string locale = "zh-cn",
        CancellationToken ct = default)
    {
        var url = $"https://www.microsoft.com/software-download-connector/api/getskuinformationbyproductedition" +
                  $"?profile={ProfileId}" +
                  $"&productEditionId={productEditionId}" +
                  $"&SKU=undefined" +
                  $"&friendlyFileName=undefined" +
                  $"&Locale={locale}" +
                  $"&sessionID={sessionId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var json = await _http.GetStringAsync(url, ct);
                var doc = JsonDocument.Parse(json);

                var result = new List<MicrosoftLanguage>();
                if (doc.RootElement.TryGetProperty("Skus", out var skus))
                {
                    foreach (var sku in skus.EnumerateArray())
                    {
                        var lang = "";
                        if (sku.TryGetProperty("Language", out var langEl))
                            lang = langEl.GetString() ?? "";
                        var skuId = 0;
                        if (sku.TryGetProperty("Id", out var idEl))
                            skuId = ParseInt32(idEl);

                        if (!string.IsNullOrEmpty(lang) && skuId > 0)
                        {
                            result.Add(new MicrosoftLanguage
                            {
                                Name = lang,
                                SkuId = skuId,
                                SessionId = sessionId
                            });
                        }
                    }
                }

                if (result.Count > 0) return result;
            }
            catch (OperationCanceledException) { throw; }
            catch { }

            await Task.Delay(2000, ct);
        }

        return [];
    }

    public static async Task<string?> GetDownloadLinkAsync(
        int skuId,
        string sessionId,
        string refererProduct = "windows11",
        CancellationToken ct = default)
    {
        var url = $"https://www.microsoft.com/software-download-connector/api/GetProductDownloadLinksBySku" +
                  $"?profile={ProfileId}" +
                  $"&productEditionId=undefined" +
                  $"&SKU={skuId}" +
                  $"&friendlyFileName=undefined" +
                  $"&Locale=zh-cn" +
                  $"&sessionID={sessionId}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Referrer = new Uri($"https://www.microsoft.com/software-download/{refererProduct}");

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("Errors", out var errors))
        {
            foreach (var err in errors.EnumerateArray())
            {
                if (err.TryGetProperty("Type", out var typeEl) && ParseInt32(typeEl) == 9)
                    throw new InvalidOperationException(
                        "微软服务器拒绝了请求（可能因 IP 区域限制）。\n" +
                        "请尝试使用 VPN 或代理后重试。\n" +
                        "错误代码: 715-123130");
            }
        }

        if (doc.RootElement.TryGetProperty("ProductDownloadOptions", out var options))
        {
            string? bestUrl = null;
            foreach (var opt in options.EnumerateArray())
            {
                if (opt.TryGetProperty("Uri", out var uriEl))
                {
                    var uri = uriEl.GetString();
                    if (!string.IsNullOrEmpty(uri))
                    {
                        if (bestUrl is null) bestUrl = uri;
                        if (opt.TryGetProperty("DownloadType", out var dtEl))
                        {
                            var dt = ParseInt32(dtEl);
                            if (dt == 1) return uri;
                        }
                    }
                }
            }
            return bestUrl;
        }

        return null;
    }

    public static async Task<WindowsImageEntry?> ResolveDownloadEntryAsync(
        MicrosoftEdition edition,
        MicrosoftLanguage language,
        CancellationToken ct = default)
    {
        var sessionId = await InitSessionAsync(ct);
        var referer = edition.Product;

        var downloadUrl = await GetDownloadLinkAsync(language.SkuId, sessionId, referer, ct);
        if (string.IsNullOrEmpty(downloadUrl)) return null;

        var fileName = "";
        try
        {
            fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            if (string.IsNullOrEmpty(fileName) || fileName.Contains('?'))
                fileName = $"{edition.Name} - {language.Name}.iso";
        }
        catch
        {
            fileName = $"{edition.Name} - {language.Name}.iso";
        }

        if (!fileName.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
            fileName += ".iso";

        return new WindowsImageEntry
        {
            DisplayName = $"{edition.Name} - {language.Name}",
            FileName = fileName,
            DownloadUrl = downloadUrl,
            SizeBytes = 0,
            SizeDisplay = "ISO",
            Category = "Microsoft 官方",
            Language = language.Name,
            Arch = edition.Arch,
            Source = WindowsImageSource.MicrosoftOfficial
        };
    }

    private static int ParseInt32(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String)
        {
            int.TryParse(el.GetString(), out var v);
            return v;
        }
        return el.TryGetInt32(out var i) ? i : 0;
    }
}
