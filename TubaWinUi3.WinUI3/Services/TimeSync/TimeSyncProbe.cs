using System.Net;
using System.Net.Sockets;

namespace TubaWinUi3.Services;

/// <summary>一次 NTP 探测结果。<see cref="OffsetMs"/> 为正表示本机时间比标准时间慢（与 NTP 协议 θ 同号）。</summary>
public sealed record NtpProbeResult
{
    public bool Ok { get; init; }
    public string Host { get; init; } = "";
    public int Stratum { get; init; }
    /// <summary>本机时间相对服务器时间的偏差（毫秒）：正值 = 本机比标准时间慢。</summary>
    public double OffsetMs { get; init; }
    /// <summary>请求往返延迟（毫秒）。</summary>
    public double RoundTripMs { get; init; }
    public DateTimeOffset ServerTimeUtc { get; init; }
    public string? Error { get; init; }

    public static NtpProbeResult Fail(string host, string error)
        => new() { Ok = false, Host = host, Error = error };
}

/// <summary>
/// SNTP 客户端（RFC 5905，UDP 123，客户端模式）。
/// 用于给用户展示「哪台服务器连得上、延迟多少、本机时间差多少」，与 w32tm 使用同一协议口径：
/// θ = ((T2 − T1) + (T3 − T4)) / 2，正值表示本机时钟慢于服务器（Windows 侧对应「正相位校正」）。
/// </summary>
public static class TimeSyncProbe
{
    private const int NtpPort = 123;
    private const int PacketSize = 48;
    /// <summary>1900-01-01 到 1970-01-01 的秒数（NTP 纪元与 Unix 纪元之差）。</summary>
    public const double NtpEpochOffsetSeconds = 2208988800d;

    public static TimeSpan DefaultTimeout => TimeSpan.FromSeconds(2.5);

    /// <summary>构造 NTP 客户端请求报文（LI=0 / VN=4 / Mode=3），发送时间戳取 <paramref name="sentLocal"/>。</summary>
    public static byte[] BuildRequest(DateTimeOffset sentLocal)
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1B;
        WriteTimestamp(packet, 40, ToUnixSeconds(sentLocal));
        return packet;
    }

    /// <summary>解析 NTP 响应并结算偏差/延迟。纯函数，便于单测。</summary>
    public static NtpProbeResult ReadResponse(byte[] packet, DateTimeOffset sentLocal, DateTimeOffset receivedLocal, string host = "")
    {
        if (packet is null || packet.Length < PacketSize)
            return NtpProbeResult.Fail(host, "响应报文不完整");

        var mode = packet[0] & 0x07;
        var stratum = packet[1];
        if (mode is not (4 or 5))
            return NtpProbeResult.Fail(host, $"响应模式异常（{mode}）");
        if (stratum == 0)
        {
            var kissCode = System.Text.Encoding.ASCII.GetString(packet, 12, 4).Trim('\0', ' ');
            return NtpProbeResult.Fail(host, $"服务器拒绝服务（{kissCode}）");
        }
        if (stratum > 15)
            return NtpProbeResult.Fail(host, "服务器层级无效");

        var t1 = ToNtpSeconds(sentLocal);
        var t2 = ReadTimestamp(packet, 32);
        var t3 = ReadTimestamp(packet, 40);
        var t4 = ToNtpSeconds(receivedLocal);
        if (t2 <= 0 || t3 <= 0)
            return NtpProbeResult.Fail(host, "响应缺少时间戳");

        var offset = ((t2 - t1) + (t3 - t4)) / 2;
        // 偏差超过一天说明服务器时间戳不可信，宁可报错也不要给出误导性数字
        if (Math.Abs(offset) > 86400)
            return NtpProbeResult.Fail(host, "服务器返回的时间不可信");

        var delay = (t4 - t1) - (t3 - t2);

        return new NtpProbeResult
        {
            Ok = true,
            Host = host,
            Stratum = stratum,
            OffsetMs = offset * 1000,
            RoundTripMs = Math.Max(0, delay * 1000),
            ServerTimeUtc = DateTimeOffset.FromUnixTimeSeconds((long)(t3 - NtpEpochOffsetSeconds))
        };
    }

    /// <summary>向单台服务器发起一次探测；失败（DNS / 超时 / 被屏蔽）返回 <see cref="NtpProbeResult.Ok"/> = false。</summary>
    public static async Task<NtpProbeResult> QueryAsync(string host, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return NtpProbeResult.Fail(host ?? "", "地址为空");

        var budget = timeout ?? DefaultTimeout;

        try
        {
            var address = await ResolveAsync(host, ct).ConfigureAwait(false);
            if (address is null)
                return NtpProbeResult.Fail(host, "域名解析失败");

            using var udp = new UdpClient(address.AddressFamily);
            udp.Connect(new IPEndPoint(address, NtpPort));

            var sentLocal = DateTimeOffset.UtcNow;
            var request = BuildRequest(sentLocal);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(budget);

            await udp.SendAsync(request.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            var response = await udp.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
            var receivedLocal = DateTimeOffset.UtcNow;

            return ReadResponse(response.Buffer, sentLocal, receivedLocal, host);
        }
        catch (OperationCanceledException)
        {
            return NtpProbeResult.Fail(host, ct.IsCancellationRequested ? "已取消" : "没有响应");
        }
        catch (SocketException ex)
        {
            return NtpProbeResult.Fail(host, DescribeSocketError(ex));
        }
        catch (Exception ex)
        {
            return NtpProbeResult.Fail(host, ex.Message);
        }
    }

    /// <summary>并发探测多台服务器（结果顺序与入参一致）。</summary>
    public static async Task<IReadOnlyList<NtpProbeResult>> QueryManyAsync(IEnumerable<string> hosts, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var list = hosts.Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var tasks = list.Select(h => QueryAsync(h, timeout, ct)).ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static string DescribeSocketError(SocketException ex) => ex.SocketErrorCode switch
    {
        SocketError.ConnectionReset => "服务器拒绝响应",
        SocketError.HostUnreachable or SocketError.NetworkUnreachable => "网络不可达（可能被防火墙或运营商屏蔽 UDP 123）",
        SocketError.TimedOut => "没有响应",
        _ => ex.Message
    };

    /// <summary>优先 IPv4：国内到部分服务器（Cloudflare/微软）的 IPv6 路径常被静默丢弃，与项目内其它网络层保持一致。</summary>
    private static async Task<IPAddress?> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal))
            return literal;

        var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault();
    }

    /// <summary>
    /// Unix 纪元秒。用 UtcTicks 换算（比 ToUnixTimeMilliseconds 多出亚毫秒精度），
    /// 注意 UtcTicks 是从公元 1 年起的刻度，必须减去 UnixEpoch 才是 Unix 时间。
    /// </summary>
    private static double ToUnixSeconds(DateTimeOffset value)
        => (value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / (double)TimeSpan.TicksPerSecond;

    /// <summary>
    /// 本机时间换算成 NTP 纪元秒（1900-01-01 起算），与报文里的时间戳同基准，
    /// 否则差值会整体差 2208988800 秒（换算基准不一致）。
    /// </summary>
    private static double ToNtpSeconds(DateTimeOffset value)
        => ToUnixSeconds(value) + NtpEpochOffsetSeconds;

    private static double ReadTimestamp(byte[] packet, int offset)
    {
        ulong seconds = ((ulong)packet[offset] << 24) | ((ulong)packet[offset + 1] << 16)
            | ((ulong)packet[offset + 2] << 8) | packet[offset + 3];
        ulong fraction = ((ulong)packet[offset + 4] << 24) | ((ulong)packet[offset + 5] << 16)
            | ((ulong)packet[offset + 6] << 8) | packet[offset + 7];
        return seconds + fraction / 4294967296d;
    }

    private static void WriteTimestamp(byte[] packet, int offset, double unixSeconds)
    {
        var ntpSeconds = unixSeconds + NtpEpochOffsetSeconds;
        var seconds = (ulong)Math.Floor(ntpSeconds);
        var fraction = (ulong)Math.Round((ntpSeconds - seconds) * 4294967296d);
        if (fraction == 4294967296ul) { seconds++; fraction = 0; }

        packet[offset] = (byte)(seconds >> 24);
        packet[offset + 1] = (byte)(seconds >> 16);
        packet[offset + 2] = (byte)(seconds >> 8);
        packet[offset + 3] = (byte)seconds;
        packet[offset + 4] = (byte)(fraction >> 24);
        packet[offset + 5] = (byte)(fraction >> 16);
        packet[offset + 6] = (byte)(fraction >> 8);
        packet[offset + 7] = (byte)fraction;
    }
}
