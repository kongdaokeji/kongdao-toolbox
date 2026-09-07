using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace TubaWinUi3.Services;

/// <summary>
/// 基于浙大测速节点（LibreSpeed，https://speedtest.zju.edu.cn）的网络测速引擎。
/// 协议与网页端 speedtest_worker.min.js 保持一致：
///   IP   : GET  /getIP.php                  -> 纯文本公网 IP
///   延迟 : GET  /empty.php?cors=true&amp;r=xxx    逐个测量 RTT
///   下载 : GET  /garbage.php?ckSize=N       N MiB 随机数据，多路并行
///   上传 : POST /empty.php                  随机数据体（服务器丢弃），多路并行
/// 速率口径统一为兆比特每秒（Mbps），含 LibreSpeed 网页端 1.06 开销补偿系数。
/// </summary>
public sealed class SpeedTestEngine : IDisposable
{
    public const string DefaultServer = "https://speedtest.zju.edu.cn";

    private const string GetIpPath = "/getIP.php";
    private const string EmptyPath = "/empty.php";
    private const string GarbagePath = "/garbage.php";
    private const double OverheadCompensation = 1.06; // TCP/IP 头开销补偿，与网页端一致
    private const int ReadBufferSize = 256 * 1024;

    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private readonly byte[] _uploadChunk;

    public int PingCount { get; init; } = 24;
    public int DownloadSeconds { get; init; } = 10;
    public int UploadSeconds { get; init; } = 8;
    public int DownloadStreams { get; init; } = 4;
    public int UploadStreams { get; init; } = 3;
    public int DownloadChunkMiB { get; init; } = 100;
    public int UploadChunkBytes { get; init; } = 1 * 1024 * 1024;

    /// <summary>实时速率回调：当前速率(Mbps)、阶段进度 0..1、已用秒数。</summary>
    public delegate void SpeedCallback(double mbps, double progress, double seconds);

    /// <summary>延迟阶段回调：当前中位延迟(ms)、当前抖动(ms)、已测次数、总数。</summary>
    public delegate void LatencyCallback(double pingMs, double jitterMs, int done, int total);

    public SpeedTestEngine(string baseUrl = DefaultServer)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(6),
            PooledConnectionLifetime = TimeSpan.FromMinutes(3),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(45),
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = false,
            MaxConnectionsPerServer = 32
        };
        _client = new HttpClient(handler)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) TubaWinUi3-SpeedTest/1.0");

        _uploadChunk = new byte[UploadChunkBytes];
        RandomNumberGenerator.Fill(_uploadChunk);
    }

    public void Dispose() => _client.Dispose();

    // ─────────────────────────── 公网 IP ───────────────────────────

    public async Task<string> GetPublicIpAsync(CancellationToken ct = default)
    {
        using var resp = await _client.GetAsync(_baseUrl + GetIpPath,
            HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return text.Trim();
    }

    // ─────────────────────────── 延迟 / 抖动 ───────────────────────────

    public async Task<(double PingMs, double JitterMs)> MeasureLatencyAsync(
        LatencyCallback? live = null, CancellationToken ct = default)
    {
        var samples = new List<double>(PingCount);
        double diffSum = 0;
        int diffCount = 0;

        for (int i = 0; i < PingCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            try
            {
                var url = _baseUrl + EmptyPath + $"?cors=true&r={Guid.NewGuid():N}";
                using var resp = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                _ = resp.StatusCode; // 任何响应均视为链路可达
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // 单次丢包/失败跳过，不中断整体测试
                await Task.Delay(15, ct).ConfigureAwait(false);
                continue;
            }
            sw.Stop();

            double ms = Math.Max(0.01, sw.Elapsed.TotalMilliseconds);
            samples.Add(ms);
            if (samples.Count >= 2)
            {
                diffSum += Math.Abs(ms - samples[^2]);
                diffCount++;
            }

            var sorted = samples.OrderBy(x => x).ToArray();
            double median = sorted.Length % 2 == 1
                ? sorted[sorted.Length / 2]
                : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0;
            double curJitter = diffCount > 0 ? diffSum / diffCount : 0.0;

            live?.Invoke(median, curJitter, samples.Count, PingCount);
            await Task.Delay(15, ct).ConfigureAwait(false);
        }

        if (samples.Count == 0)
            return (double.NaN, double.NaN);

        var final = samples.OrderBy(x => x).ToArray();
        double ping = final.Length % 2 == 1
            ? final[final.Length / 2]
            : (final[final.Length / 2 - 1] + final[final.Length / 2]) / 2.0;
        double jitter = diffCount > 0 ? diffSum / diffCount : 0.0;
        return (ping, jitter);
    }

    // ─────────────────────────── 下载 ───────────────────────────

    public async Task<double> MeasureDownloadAsync(SpeedCallback? live, CancellationToken ct)
    {
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        long bytes = 0;
        var sw = Stopwatch.StartNew();

        var streams = new Task[DownloadStreams];
        for (int s = 0; s < DownloadStreams; s++)
        {
            streams[s] = Task.Run(
                () => DownloadStreamWorkerAsync(phaseCts.Token, n => Interlocked.Add(ref bytes, n)),
                CancellationToken.None);
        }

        await RunSamplerAsync(sw, phaseCts,
            () => Volatile.Read(ref bytes),
            () => Math.Min(1.0, sw.Elapsed.TotalSeconds / DownloadSeconds),
            DownloadSeconds,
            live).ConfigureAwait(false);

        await WaitStreamsGracefullyAsync(streams, ct, TimeSpan.FromSeconds(2.5)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested(); // 用户主动停止时向调用方传播取消，而不是返回残缺结果
        return BitsToMbps(Volatile.Read(ref bytes), sw.Elapsed.TotalSeconds);
    }

    private async Task DownloadStreamWorkerAsync(CancellationToken ct, Action<long> add)
    {
        var buffer = new byte[ReadBufferSize];
        var target = _baseUrl + GarbagePath + $"?ckSize={DownloadChunkMiB}";
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var resp = await _client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    await Task.Delay(60, ct).ConfigureAwait(false);
                    continue;
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                while (!ct.IsCancellationRequested)
                {
                    int n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (n <= 0) break;
                    add(n);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { /* 单流失败静默，由整体速率体现 */ }
    }

    // ─────────────────────────── 上传 ───────────────────────────

    public async Task<double> MeasureUploadAsync(SpeedCallback? live, CancellationToken ct)
    {
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        long sent = 0;
        var sw = Stopwatch.StartNew();

        var streams = new Task[UploadStreams];
        for (int s = 0; s < UploadStreams; s++)
        {
            streams[s] = Task.Run(
                () => UploadStreamWorkerAsync(ct, phaseCts.Token, n => Interlocked.Add(ref sent, n)),
                CancellationToken.None);
        }

        await RunSamplerAsync(sw, phaseCts,
            () => Volatile.Read(ref sent),
            () => Math.Min(1.0, sw.Elapsed.TotalSeconds / UploadSeconds),
            UploadSeconds,
            live).ConfigureAwait(false);

        // 自然结束后不再发起新请求，但放行在途上传块完成并计数，避免尾部吞吐被低估
        await WaitStreamsGracefullyAsync(streams, ct, TimeSpan.FromSeconds(6)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return BitsToMbps(Volatile.Read(ref sent), sw.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// 单路上传流：<paramref name="stopNewCt"/> 控制“是否继续发起新请求”（自然结束时取消），
    /// <paramref name="ct"/> 控制“立即中止在途请求”（用户主动停止）。
    /// </summary>
    private async Task UploadStreamWorkerAsync(CancellationToken ct, CancellationToken stopNewCt, Action<long> add)
    {
        var target = _baseUrl + EmptyPath;
        try
        {
            while (!stopNewCt.IsCancellationRequested)
            {
                try
                {
                    using var content = new ByteArrayContent(_uploadChunk);
                    using var resp = await _client.PostAsync(target, content, ct).ConfigureAwait(false);
                    _ = resp.StatusCode;
                    add(UploadChunkBytes);
                }
                catch (OperationCanceledException) when (!stopNewCt.IsCancellationRequested)
                {
                    // 仅用户主动取消时退出；自然结束后的“收尾在途请求”不受 stopNewCt 影响
                    break;
                }
                catch (OperationCanceledException) { break; }
                catch { break; } // 单流持续失败时退出，避免空转
            }
        }
        catch { /* 静默 */ }
    }

    // ─────────────────────────── 采样器 / 工具 ───────────────────────────

    private async Task RunSamplerAsync(
        Stopwatch sw,
        CancellationTokenSource phaseCts,
        Func<long> totalBytes,
        Func<double> progress,
        double totalSeconds,
        SpeedCallback? live)
    {
        // 1 秒滑动窗口：上传按块离散到达、下载有响应间隙，瞬时差分会大幅抖动，
        // 窗口平均既平滑又贴近真实持续吞吐。
        const double windowSeconds = 1.0;
        var window = new Queue<(double T, long Bytes)>();

        try
        {
            while (true)
            {
                await Task.Delay(120, phaseCts.Token).ConfigureAwait(false);
                double now = sw.Elapsed.TotalSeconds;
                long b = totalBytes();
                window.Enqueue((now, b));
                while (window.Count > 1 && now - window.Peek().T > windowSeconds)
                    window.Dequeue();

                double liveMbps = 0;
                var oldest = window.Peek();
                double dt = now - oldest.T;
                if (dt >= 0.2 && b >= oldest.Bytes)
                {
                    liveMbps = BitsToMbps(b - oldest.Bytes, dt);
                    if (liveMbps < 0) liveMbps = 0;
                }
                live?.Invoke(liveMbps, Math.Clamp(progress(), 0, 1), now);

                if (now >= totalSeconds)
                    break;
            }
        }
        catch (OperationCanceledException) { }

        // 停止发起新请求，允许在途请求收尾后再统计
        phaseCts.Cancel();
    }

    private static async Task WaitStreamsGracefullyAsync(Task[] streams, CancellationToken ct, TimeSpan timeout)
    {
        try
        {
            await Task.WhenAll(streams).WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { }
        catch { }
    }

    private static double BitsToMbps(long bytes, double seconds)
    {
        if (seconds <= 0.02 || bytes <= 0) return 0;
        return bytes * 8.0 / 1e6 / seconds * OverheadCompensation;
    }
}
