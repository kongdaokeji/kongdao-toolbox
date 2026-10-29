using System.Net;
using System.Net.Sockets;

namespace TubaWinUi3.Services;

/// <summary>
/// 创建 IPv4 优先的 HttpClient：国内网络到国际 CDN（Cloudflare、微软等）的 IPv6 路径常被静默丢弃
/// （SYN 无响应），而 SocketsHttpHandler 按顺序尝试解析出的地址（无双栈并行），默认 IPv6 优先时
/// 会白等一个完整的超时周期，表现为请求长时间卡住后超时。
/// 该工厂只查询 A 记录并建 IPv4 连接；无 A 记录时回退全家族解析，IPv6-only 网络不受影响。
/// 同一域名解析出的多个 IP 会逐个尝试（每个 IP 独立 3 秒预算，整体 9 秒封顶），
/// 避免 ISP 返回的第一个 IP 不可达时直接失败。
/// </summary>
public static class HttpClientFactory
{
    public static HttpClient CreateIpv4Preferred(TimeSpan timeout)
    {
        return new HttpClient(CreateIpv4PreferredHandler())
        {
            Timeout = timeout,
        };
    }

    /// <summary>只建 handler 不建客户端：供第三方下载库（如 Downloader 的 CustomHttpMessageHandlerFactory）注入。</summary>
    public static SocketsHttpHandler CreateIpv4PreferredHandler()
    {
        return new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = ConnectAsync,
        };
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var host = ctx.DnsEndPoint.Host;
        var port = ctx.DnsEndPoint.Port;

        // 整体连接预算（含 DNS 解析）9 秒；外部取消优先冒泡
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(9));

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, budget.Token)
                .ConfigureAwait(false);
            if (addresses.Length == 0)
                addresses = await Dns.GetHostAddressesAsync(host, budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 仅查 A 记录失败（如 IPv6-only 网络）时退回系统默认解析
            addresses = await Dns.GetHostAddressesAsync(host, budget.Token).ConfigureAwait(false);
        }
        if (addresses.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            // 每个 IP 短暂预算内完成连接，一个坏 IP 不耗尽整体预算
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
            attempt.CancelAfter(TimeSpan.FromSeconds(3));

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), attempt.Token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                socket.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                socket.Dispose();
            }
        }

        throw lastError ?? new SocketException((int)SocketError.HostNotFound);
    }
}