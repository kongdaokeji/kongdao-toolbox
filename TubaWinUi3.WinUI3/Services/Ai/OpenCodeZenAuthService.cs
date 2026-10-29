using System.Net.Http;
using System.Text.Json;

namespace TubaWinUi3.Services.Ai;

/// <summary>
/// OpenCode Zen 免费模型服务。
/// 免费模型（*-free / big-pickle）匿名（Authorization: Bearer public）即可调用，但匿名额度较低；
/// 带 API Key（sk-…，通过 <see cref="OpenCodeZenLoginDialog"/> 登录自动获取）调用额度大幅提升。
/// 本服务负责从 <c>https://opencode.ai/zen/v1/models</c> 刷新免费模型列表。
/// </summary>
public static class OpenCodeZenAuthService
{
    private const string ZenApiBase = "https://opencode.ai/zen/v1";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    static OpenCodeZenAuthService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-OpenCodeZen");
    }

    /// <summary>
    /// 刷新 OpenCode Zen 免费模型列表（GET /zen/v1/models，匿名即可）。
    /// 免费模型 = Id 以 -free 结尾或 big-pickle；保留用户手动添加的非免费模型。
    /// </summary>
    public static async Task<(int Count, string? Error)> RefreshFreeModelsAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ZenApiBase}/models");
            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return (0, $"HTTP {(int)resp.StatusCode}：{Truncate(body, 200)}");

            var freeIds = new List<string>();
            using (var doc = JsonDocument.Parse(body))
            {
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idProp) &&
                            idProp.GetString() is { } id &&
                            AiProviderStore.IsFreeModelId(id))
                        {
                            freeIds.Add(id);
                        }
                    }
                }
            }

            var provider = AiProviderStore.GetProvider(AiProviderStore.OpenCodeZenProviderId);
            if (provider is null) return (0, "OpenCode Zen 提供商不存在");

            if (freeIds.Count > 0)
            {
                // 保留用户手动添加的非免费模型，免费模型以服务端列表为准
                var manual = provider.Models.Where(m => !AiProviderStore.IsFreeModelId(m.Id)).ToList();
                provider.Models = manual
                    .Concat(freeIds.Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(id => new AiModelOption(id)))
                    .ToList();
                if (!provider.Models.Any(m => m.Id == provider.DefaultModel))
                    provider.DefaultModel = freeIds[0];
                AiProviderStore.Save();
            }

            return (freeIds.Count, null);
        }
        catch (Exception ex)
        {
            return (0, ex.Message);
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
