using System.Net;

namespace TubaWinUi3.Services;

public static class ProxyService
{
    public static bool IsProxyEnabled => AppSettings.GetBool("ProxyEnabled");
    
    public static string? ProxyAddress => AppSettings.Get("ProxyAddress");
    
    public static string? ProxyUsername => AppSettings.Get("ProxyUsername");
    
    public static string? ProxyPassword => AppSettings.Get("ProxyPassword");

    public static bool HasProxy => IsProxyEnabled && !string.IsNullOrWhiteSpace(ProxyAddress);

    private static HttpClientHandler? CreateHandler()
    {
        if (!HasProxy) return null;
        
        var handler = new HttpClientHandler();
        var proxy = new WebProxy(ProxyAddress!);
        
        if (!string.IsNullOrWhiteSpace(ProxyUsername))
        {
            proxy.Credentials = new NetworkCredential(ProxyUsername, ProxyPassword ?? "");
        }
        
        handler.Proxy = proxy;
        handler.UseProxy = true;
        
        return handler;
    }

    public static HttpClient CreateClient(TimeSpan? timeout = null)
    {
        var handler = CreateHandler();
        var client = handler is not null
            ? new HttpClient(handler)
            : new HttpClient();
        
        client.Timeout = timeout ?? TimeSpan.FromSeconds(30);
        return client;
    }

    public static HttpClient CreateClientWithHeaders(TimeSpan? timeout = null, params (string name, string value)[] headers)
    {
        var client = CreateClient(timeout);
        foreach (var (name, value) in headers)
        {
            if (!client.DefaultRequestHeaders.Contains(name))
                client.DefaultRequestHeaders.Add(name, value);
        }
        return client;
    }

    public static WebProxy? GetWebProxy()
    {
        if (!HasProxy) return null;
        
        var proxy = new WebProxy(ProxyAddress!);
        
        if (!string.IsNullOrWhiteSpace(ProxyUsername))
        {
            proxy.Credentials = new NetworkCredential(ProxyUsername, ProxyPassword ?? "");
        }
        
        return proxy;
    }

    public static void ApplyProxyToRequest(HttpRequestMessage request)
    {
        // HTTP request 代理通过 HttpClientHandler 设置，这里仅做标记
    }
}