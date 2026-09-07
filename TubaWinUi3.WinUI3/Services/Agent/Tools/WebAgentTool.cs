using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 网络工具：联网搜索、网页抓取（复用 <see cref="WebSearchService"/>）、
/// 文件下载（复用 <see cref="ProxyService"/> 代理配置）。
/// </summary>
public static class WebAgentTool
{
    public static void Register()
    {
        Add("web_search", "联网搜索", "\uE721", (Func<string, CancellationToken, Task<string>>)WebSearchAsync);
        Add("fetch_page", "访问网页", "\uE774", (Func<string, CancellationToken, Task<string>>)FetchPageAsync);
        Add("download_file", "下载文件", "\uE896", (Func<string, string, string, CancellationToken, Task<string>>)DownloadFileAsync);
    }

    [Description("联网搜索，获取最新硬件评测、驱动、新闻、价格等（涉及最新信息时必须使用！）。关键词可中英混合，如 \"Intel Core Ultra 9 285K 评测 性能\"")]
    public static async Task<string> WebSearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "错误：缺少 query 参数，请提供搜索关键词";

        try
        {
            var result = await WebSearchService.SearchAsync(query, ct);
            return WebSearchService.FormatResult(result);
        }
        catch (OperationCanceledException)
        {
            return "搜索已取消";
        }
        catch (Exception ex)
        {
            return $"搜索失败：{ex.Message}";
        }
    }

    [Description("访问网页获取完整文本内容（搜索结果摘要不够详细时使用）")]
    public static async Task<string> FetchPageAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "错误：缺少 url 参数，请提供要访问的网页 URL";

        try
        {
            var page = await WebSearchService.FetchWebPageAsync(url, ct);
            var sb = new StringBuilder();
            sb.AppendLine($"页面标题：{page.Title}");
            sb.AppendLine($"URL：{page.Url}");
            sb.AppendLine($"内容格式：{page.ContentType}");
            sb.AppendLine();
            sb.AppendLine(page.Content);
            return sb.ToString();
        }
        catch (OperationCanceledException)
        {
            return "页面获取已取消";
        }
        catch (Exception ex)
        {
            return $"获取页面失败：{ex.Message}";
        }
    }

    [Description("下载文件到本地路径（需用户确认后执行；支持 http/https，最大 2GB）")]
    public static async Task<string> DownloadFileAsync(string url, string destinationPath, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return "错误：缺少 url 参数";
        if (FileSandbox.ValidateWrite(destinationPath) is { } err) return $"错误：{err}";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "错误：URL 无效（仅支持 http/https）";
        }

        try
        {
            using var client = ProxyService.CreateClient(TimeSpan.FromMinutes(10));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return $"下载失败：HTTP {(int)response.StatusCode}";

            var length = response.Content.Headers.ContentLength ?? 0;
            if (length > 2L * 1024 * 1024 * 1024)
                return "错误：文件超过 2GB 下载上限";

            var full = Path.GetFullPath(destinationPath);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            await using var fs = File.Create(full);
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await src.CopyToAsync(fs, ct);

            var sizeText = length > 0 ? AgentToolHelpers.FormatSize(length) : "（大小未知）";
            return $"下载完成：{full}（{sizeText}）";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return "下载已取消";
        }
        catch (Exception ex)
        {
            return $"下载失败：{ex.Message}";
        }
    }

    private static void Add(string name, string displayName, string glyph, Delegate method)
    {
        AgentToolRegistry.Register(new AgentTool
        {
            Name = name,
            DisplayName = displayName,
            Glyph = glyph,
            Function = AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = name }),
            RequiresConfirmation = name == "download_file",
            ConfirmKind = name == "download_file" ? "download_file" : null,
            DefaultReason = name == "download_file" ? "AI 请求下载此文件" : null,
        });
    }
}
