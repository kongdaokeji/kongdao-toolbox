namespace TubaWinUi3.Services;

/// <summary>文件传输方向（手机 → 电脑 = Upload，电脑 → 手机 = Download）。</summary>
public enum PhoneTransferDirection
{
    Upload,
    Download
}

/// <summary>一次文件传输的实时进度（聊天弹窗用它显示百分比与速度）。</summary>
public sealed class PhoneTransferProgress
{
    public string Id { get; init; } = "";
    public string FileName { get; init; } = "";
    public PhoneTransferDirection Direction { get; init; }
    public long Bytes { get; init; }
    /// <summary>总字节数；未知（无 Content-Length）时为 -1。</summary>
    public long TotalBytes { get; init; } = -1;
    public double BytesPerSecond { get; init; }
    public bool Completed { get; init; }
    public bool Failed { get; init; }

    /// <summary>速度显示文本（B/s · KB/s · MB/s · GB/s）。</summary>
    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:F0} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024:F1} KB/s";
        if (bytesPerSecond < 1024L * 1024 * 1024) return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
        return $"{bytesPerSecond / (1024 * 1024 * 1024):F2} GB/s";
    }
}

/// <summary>
/// 传输速度上报器：按最小间隔节流，用字节增量算瞬时速度（完成时给全程平均速度）。
/// 时钟可注入，纯逻辑可单测。
/// </summary>
internal sealed class TransferSpeedReporter
{
    private readonly string _id;
    private readonly string _fileName;
    private readonly PhoneTransferDirection _direction;
    private readonly long _totalBytes;
    private readonly Action<PhoneTransferProgress> _sink;
    private readonly long _minIntervalMs;
    private readonly Func<long> _nowMs;

    private readonly long _startedMs;
    private long _lastMs;
    private long _lastBytes;
    private bool _finished;

    public TransferSpeedReporter(string id, string fileName, PhoneTransferDirection direction,
        long totalBytes, Action<PhoneTransferProgress> sink, Func<long>? nowMs = null, long minIntervalMs = 200)
    {
        _id = id;
        _fileName = fileName;
        _direction = direction;
        _totalBytes = totalBytes;
        _sink = sink;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
        _minIntervalMs = minIntervalMs;
        _startedMs = _lastMs = _nowMs();
    }

    /// <summary>传输中按节流间隔上报（force = true 立即上报）。</summary>
    public void Report(long bytes, bool force = false)
    {
        if (_finished) return;
        var now = _nowMs();
        if (!force && now - _lastMs < _minIntervalMs) return;
        var deltaMs = now - _lastMs;
        var speed = deltaMs > 0 ? Math.Max(0, (bytes - _lastBytes) * 1000.0 / deltaMs) : 0;
        _lastMs = now;
        _lastBytes = bytes;
        Emit(bytes, speed, completed: false, failed: false);
    }

    /// <summary>传输完成：上报全程平均速度。</summary>
    public void Complete(long bytes)
    {
        if (_finished) return;
        _finished = true;
        var elapsedMs = _nowMs() - _startedMs;
        var average = elapsedMs > 0 ? bytes * 1000.0 / elapsedMs : 0;
        Emit(bytes, average, completed: true, failed: false);
    }

    /// <summary>传输失败/中断。</summary>
    public void Fail(long bytes)
    {
        if (_finished) return;
        _finished = true;
        Emit(bytes, 0, completed: false, failed: true);
    }

    private void Emit(long bytes, double speed, bool completed, bool failed) =>
        _sink(new PhoneTransferProgress
        {
            Id = _id,
            FileName = _fileName,
            Direction = _direction,
            Bytes = bytes,
            TotalBytes = _totalBytes,
            BytesPerSecond = speed,
            Completed = completed,
            Failed = failed
        });
}
