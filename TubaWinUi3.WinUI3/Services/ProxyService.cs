using System.Net;

namespace TubaWinUi3.Services;

public static class ProxyService
{
    public static bool IsProxyEnabled => AppSettings.GetBool("ProxyEnabled");
    
    public static string? ProxyAddress => AppSettings.Get("ProxyAddress");
    
    public static string? ProxyUsername => AppSettings.Get("ProxyUsername");
    
    public static string? ProxyPassword => AppSettings.Get("ProxyPassword");

    public static bool HasProxy => IsProxyEnabled && !string.IsNullOrWhiteSpace(ProxyAddress);

    private static readonly object ClientLock = new();
    private static HttpClientHandler? s_sharedHandler;
    private static string? s_handlerSignature;

    /// <summary>共享 handler：连接池/TLS 会话跨请求复用；代理设置变化（签名变化）时才重建。</summary>
    private static HttpClientHandler GetOrCreateHandler()
    {
        var signature = HasProxy
            ? $"{ProxyAddress}|{ProxyUsername}|{ProxyPassword}"
            : "";

        lock (ClientLock)
        {
            if (s_sharedHandler is not null && signature == s_handlerSignature)
                return s_sharedHandler;

            // 旧 handler 不主动 Dispose：可能仍有在途请求占用，交给 GC 回收
            var handler = new HttpClientHandler();
            if (HasProxy)
            {
                var proxy = new WebProxy(ProxyAddress!);

                if (!string.IsNullOrWhiteSpace(ProxyUsername))
                {
                    proxy.Credentials = new NetworkCredential(ProxyUsername, ProxyPassword ?? "");
                }

                handler.Proxy = proxy;
                handler.UseProxy = true;
            }

            s_sharedHandler = handler;
            s_handlerSignature = signature;
            return handler;
        }
    }

    public static HttpClient CreateClient(TimeSpan? timeout = null)
    {
        // disposeHandler: false —— 调用方 using 释放 client 时不会连带释放共享连接池
        var client = new HttpClient(GetOrCreateHandler(), disposeHandler: false);

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