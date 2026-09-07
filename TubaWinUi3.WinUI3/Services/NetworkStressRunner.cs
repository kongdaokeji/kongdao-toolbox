using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TubaWinUi3.Services;

public enum NetStressTarget { Auto, UnicastGateway, Broadcast }

/// <summary>
/// 网卡烤机：以指定速率（MB/s）向目标持续发送 UDP 数据包，
/// 对网卡产生大量数据交流，运行到设定时长后自动停止，实时统计收发速率与累计数据量。
/// 单播：发给路由器（发送优先，WiFi 下实测可达链路的 50% 左右）；
/// 广播：发给子网广播地址，交换机/AP 会把帧回环到本机网卡，有线千兆可全双工跑满。
/// </summary>
public sealed class NetworkStressRunner : IDisposable
{
    public const int DefaultPort = 56789;
    private const int SenderCount = 4;
    private const int MaxPayload = 65507;
    private static readonly IPEndPoint BroadcastTarget = new(IPAddress.Parse("255.255.255.255"), DefaultPort);

    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _tasks = [];
    private readonly List<Socket> _sockets = [];
    private readonly object _rateLock = new();

    private long _bytesSent;
    private long _bytesReceived;
    private long _datagramsSent;
    private long _sentSnapshot;
    private long _recvSnapshot;
    private double _sendRateBps;
    private double _recvRateBps;

    public bool IsActive { get; private set; }
    public string FinishedReason { get; private set; } = "";
    public string ErrorMessage { get; private set; } = "";
    public string WarningMessage { get; private set; } = "";
    public string TargetName { get; private set; } = "";
    public bool IsUnicast { get; private set; }

    public long BytesSent => Interlocked.Read(ref _bytesSent);
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);
    public long DatagramsSent => Interlocked.Read(ref _datagramsSent);

    public double SendRateBps { get { lock (_rateLock) return _sendRateBps; } }
    public double ReceiveRateBps { get { lock (_rateLock) return _recvRateBps; } }

    /// <summary>
    /// 自动模式：有线网卡用广播（交换机回环可全双工跑满），无线网卡用单播（WiFi 广播会被 AP 限速）。
    /// 手动模式按用户选择执行。
    /// </summary>
    private IPEndPoint ResolveTarget(NetStressTarget mode)
    {
        if (mode == NetStressTarget.Broadcast)
        {
            TargetName = "子网广播 255.255.255.255（交换机/AP 回环，收发双工）";
            IsUnicast = false;
            return BroadcastTarget;
        }

        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(x => x.OperationalStatus == OperationalStatus.Up &&
                                     x.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)))
            {
                var gw = n.GetIPProperties().GatewayAddresses.FirstOrDefault()?.Address;
                if (gw is null || gw.Equals(IPAddress.Any) || !gw.GetAddressBytes().Any(b => b != 0)) continue;

                var wireless = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                if (mode == NetStressTarget.UnicastGateway || wireless)
                {
                    TargetName = $"路由器 {gw}（{n.Name}，单播发送优先）";
                    IsUnicast = true;
                    return new IPEndPoint(gw, DefaultPort);
                }
            }
        }
        catch { }
        TargetName = "子网广播 255.255.255.255（交换机/AP 回环，收发双工）";
        IsUnicast = false;
        return BroadcastTarget;
    }

    /// <summary>
    /// 启动网卡烤机（后台任务，立即返回）。
    /// </summary>
    /// <param name="mbps">每秒数据量 (MB/s)，各发送线程按总速率分摊限速</param>
    /// <param name="maxMinutes">最长运行时长 (分钟)</param>
    /// <param name="mode">目标模式：自动 / 单播路由器 / 广播局域网</param>
    /// <returns>true 表示发送已启动；false 表示启动失败（ErrorMessage 含原因）</returns>
    public bool Start(double mbps, double maxMinutes, NetStressTarget mode = NetStressTarget.Auto)
    {
        if (IsActive) return true;
        IsActive = true;
        FinishedReason = "";
        ErrorMessage = "";
        WarningMessage = "";

        var rateBps = mbps > 0 ? mbps * 1024d * 1024d : 0;
        var target = ResolveTarget(mode);

        Socket? receiver = null;
        try
        {
            receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            receiver.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            receiver.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            receiver.ReceiveBufferSize = 4 * 1024 * 1024;
            receiver.Bind(new IPEndPoint(IPAddress.Any, DefaultPort));
            lock (_rateLock) _sockets.Add(receiver);
        }
        catch (Exception ex)
        {
            WarningMessage = $"接收监听初始化失败（将仅发送不测接收）: {ex.Message}";
            receiver?.Dispose();
        }

        var senders = new List<Socket>();
        try
        {
            for (int i = 0; i < SenderCount; i++)
            {
                var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                sender.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                sender.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                sender.SendBufferSize = 4 * 1024 * 1024;
                lock (_rateLock) _sockets.Add(sender);
                senders.Add(sender);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"网卡烤机初始化失败: {ex.Message}";
            IsActive = false;
            CloseSockets();
            return false;
        }

        if (receiver is not null)
            _tasks.Add(Task.Run(() => RunReceiver(receiver, _cts.Token)));
        for (int i = 0; i < senders.Count; i++)
        {
            var s = senders[i];
            _tasks.Add(Task.Run(() => RunSender(s, target, rateBps / SenderCount, _cts.Token)));
        }
        _tasks.Add(Task.Run(() => RunSupervisor(maxMinutes, _cts.Token)));
        return true;
    }

    public void Stop()
    {
        try { _cts.Cancel(); } catch { }
        CloseSockets();
        try { Task.WaitAll([.. _tasks], 1500); } catch { }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        CloseSockets();
        _cts.Dispose();
        try { Task.WaitAll([.. _tasks], 1500); } catch { }
    }

    private void RunSender(Socket sender, IPEndPoint target, double rateLimitBps, CancellationToken token)
    {
        try
        {
            var payload = new byte[MaxPayload];
            new Random().NextBytes(payload);
            var sw = Stopwatch.StartNew();
            long sent = 0;

            while (!token.IsCancellationRequested)
            {
                sender.SendTo(payload, target);
                Interlocked.Add(ref _bytesSent, payload.Length);
                Interlocked.Increment(ref _datagramsSent);
                sent += payload.Length;

                if (rateLimitBps > 0)
                {
                    var elapsed = sw.Elapsed.TotalSeconds;
                    if (elapsed > 0)
                    {
                        var expected = elapsed * rateLimitBps;
                        if (sent > expected)
                            Thread.Sleep((int)Math.Min(25, Math.Max(0.5, (sent - expected) / rateLimitBps * 1000)));
                    }
                }
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            lock (_rateLock)
            {
                if (ErrorMessage == "") ErrorMessage = $"发送线程异常: {ex.Message}";
            }
        }
    }

    private void RunReceiver(Socket receiver, CancellationToken token)
    {
        try
        {
            var buffer = new byte[MaxPayload + 128];
            while (!token.IsCancellationRequested)
            {
                var n = receiver.Receive(buffer);
                if (n > 0) Interlocked.Add(ref _bytesReceived, n);
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            lock (_rateLock)
            {
                if (ErrorMessage == "") ErrorMessage = $"接收线程异常: {ex.Message}";
            }
        }
    }

    private void RunSupervisor(double maxMinutes, CancellationToken token)
    {
        var durationSw = Stopwatch.StartNew();
        var rateSw = Stopwatch.StartNew();

        while (!token.IsCancellationRequested)
        {
            Thread.Sleep(200);

            if (maxMinutes > 0 && durationSw.Elapsed.TotalMinutes >= maxMinutes)
            {
                FinishedReason = $"达到设定时长 {maxMinutes:0.#} 分钟";
                break;
            }

            if (rateSw.Elapsed.TotalSeconds >= 0.5)
            {
                var dt = rateSw.Elapsed.TotalSeconds;
                var sent = BytesSent;
                var recv = BytesReceived;
                lock (_rateLock)
                {
                    _sendRateBps = (sent - _sentSnapshot) / dt;
                    _recvRateBps = (recv - _recvSnapshot) / dt;
                }
                _sentSnapshot = sent;
                _recvSnapshot = recv;
                rateSw.Restart();
            }
        }

        try { _cts.Cancel(); } catch { }
        CloseSockets();
        IsActive = false;
    }

    private void CloseSockets()
    {
        List<Socket> sockets;
        lock (_rateLock)
        {
            sockets = [.. _sockets];
            _sockets.Clear();
        }
        foreach (var s in sockets)
        {
            try { s.Close(); } catch { }
            try { s.Dispose(); } catch { }
        }
    }
}
