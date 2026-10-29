using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>一次 w32tm / net 调用的结果。</summary>
internal sealed record CliOutput(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;

    public string Combined => string.IsNullOrWhiteSpace(StdErr)
        ? StdOut
        : string.IsNullOrWhiteSpace(StdOut) ? StdErr : StdOut.TrimEnd() + "\n" + StdErr.TrimEnd();

    /// <summary>取第一行有内容的输出，用于提示（过长时截断）。</summary>
    public string FirstLine(string fallback = "（无输出）")
    {
        var text = !string.IsNullOrWhiteSpace(StdErr) ? StdErr : StdOut;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
        }
        return fallback;
    }
}

/// <summary>
/// w32tm.exe / net.exe 的命令封装与输出解析。只负责「跑命令 + 解析」，
/// 语义编排在 <see cref="TimeSyncService"/>。所有命令都是短命进程，无退出清理负担。
/// 官方命令参考：https://learn.microsoft.com/windows-server/networking/windows-time-service/windows-time-service-tools-and-settings
/// </summary>
internal static class W32TimeCli
{
    private static Encoding? _consoleEncoding;

    /// <summary>
    /// 控制台程序按系统代码页输出（中文系统为 GBK），.NET 默认的 UTF-8 会把中文读成乱码；
    /// 与项目内其它进程调用保持同一套处理（注册 CodePages 提供程序后取 OEM 代码页）。
    /// </summary>
    public static Encoding ConsoleEncoding
    {
        get
        {
            if (_consoleEncoding is not null) return _consoleEncoding;
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _consoleEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            }
            catch
            {
                try { _consoleEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage); }
                catch { _consoleEncoding = Encoding.UTF8; }
            }
            return _consoleEncoding;
        }
    }

    public static Task<CliOutput> W32TmAsync(TimeSpan timeout, params string[] args)
        => RunAsync("w32tm.exe", args, timeout);

    /// <summary>启动 / 停止 Windows 时间服务（官方文档使用的 net stop / net start w32time）。</summary>
    public static Task<CliOutput> NetServiceAsync(string verb, string serviceName, TimeSpan timeout, CancellationToken ct = default)
        => RunAsync("net.exe", [verb, serviceName], timeout, ct);

    /// <summary>运行外部命令；超时或取消时连同子进程树一起结束，不留孤儿进程。</summary>
    public static async Task<CliOutput> RunAsync(string fileName, string[] args, TimeSpan timeout, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ConsoleEncoding,
            StandardErrorEncoding = ConsoleEncoding,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start()) return new CliOutput(-1, "", $"无法启动 {fileName}");
        }
        catch (Exception ex)
        {
            return new CliOutput(-1, "", ex.Message);
        }

        // 双流并发读取：只读一路会在另一路填满管道缓冲区时死锁
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            var partialOut = await SafeReadAsync(stdoutTask).ConfigureAwait(false);
            var partialErr = await SafeReadAsync(stderrTask).ConfigureAwait(false);
            var reason = ct.IsCancellationRequested ? "命令已取消" : $"{timeout.TotalSeconds:F0} 秒内没有响应";
            return new CliOutput(-1, partialOut, string.IsNullOrWhiteSpace(partialErr) ? reason : partialErr);
        }

        var stdout = await SafeReadAsync(stdoutTask).ConfigureAwait(false);
        var stderr = await SafeReadAsync(stderrTask).ConfigureAwait(false);
        return new CliOutput(process.ExitCode, stdout, stderr);
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try { return await task.ConfigureAwait(false); }
        catch { return ""; }
    }

    /// <summary>把参数拼成可直接复制到命令行的文本（引号规则按 Windows 习惯简化）。</summary>
    public static string Describe(string fileName, string[] args)
        => fileName + " " + string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

    /// <summary>把 w32tm 退出码翻成中文说明。</summary>
    public static string DescribeExitCode(int exitCode) => exitCode switch
    {
        0 => "成功",
        5 => "拒绝访问（需要管理员权限）",
        2 => "命令用法不正确",
        87 => "参数不正确",
        1290 or 1062 => "Windows 时间服务没有运行",
        _ => $"退出码 {exitCode}"
    };

    /// <summary>`w32tm /query /source` 的输出就是时间源本身（可能带一行说明文本，取第一行有效内容）。</summary>
    public static string ParseSource(string stdout)
    {
        foreach (var line in stdout.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed;
        }
        return "";
    }

    /// <summary>
    /// 从 `w32tm /query /status` 输出里取当前时间源。
    /// 未提权时 `/query /source` 会返回「拒绝访问」，而 /status 仍然可用且带「源:」一行，
    /// 所以优先用 /status 的标签行，标签跟随系统语言（中文「源」/ 英文 Source）。
    /// </summary>
    public static string ParseSourceFromStatus(string statusStdout)
    {
        if (string.IsNullOrWhiteSpace(statusStdout)) return "";

        foreach (var raw in statusStdout.Split('\n'))
        {
            var line = raw.Trim();
            var matchesLabel = line.StartsWith("源", StringComparison.Ordinal)
                || line.StartsWith("Source", StringComparison.OrdinalIgnoreCase);
            if (!matchesLabel) continue;

            var index = line.IndexOf(':');
            if (index < 0 || index == line.Length - 1) continue;

            var value = line[(index + 1)..].Trim();
            if (value.Length == 0) continue;
            if (value.StartsWith("IP", StringComparison.OrdinalIgnoreCase)) continue; // 「源 IP」不是时间源
            return value;
        }

        return "";
    }

    /// <summary>判断时间源是不是「本机 CMOS 时钟」这类未同步任何网络源的状态。</summary>
    public static bool LooksLikeLocalClock(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        var value = source.Trim();
        return value.Contains("CMOS", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Local", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Free-running", StringComparison.OrdinalIgnoreCase)
            || value.Contains("本地", StringComparison.Ordinal)
            || value.Contains("未同步", StringComparison.Ordinal);
    }

    /// <summary>
    /// 从 `w32tm /query /status` 输出里取「上次成功同步时间」。
    /// 输出标签会跟随系统语言（中文为「上次成功同步时间」），因此先按标签匹配，
    /// 匹配不到时退化为「扫一行能解析成日期时间的内容」。
    /// </summary>
    public static DateTimeOffset? ParseLastSyncTime(string statusStdout)
    {
        if (string.IsNullOrWhiteSpace(statusStdout)) return null;

        var lines = statusStdout.Split('\n');
        var byLabel = TryParseLabeled(lines, ["上次成功同步时间", "Last Successful Sync Time"]);
        if (byLabel is not null) return byLabel;

        foreach (var line in lines)
        {
            var index = line.IndexOf(':');
            if (index < 0 || index == line.Length - 1) continue;
            var candidate = line[(index + 1)..].Trim();
            if (!candidate.Contains('/') && !candidate.Contains('-')) continue;
            if (TryParseDateTime(candidate, out var parsed)) return parsed;
        }

        return null;
    }

    private static DateTimeOffset? TryParseLabeled(string[] lines, string[] labels)
    {
        foreach (var line in lines)
        {
            foreach (var label in labels)
            {
                if (!line.Contains(label, StringComparison.OrdinalIgnoreCase)) continue;
                var index = line.IndexOf(':');
                if (index < 0 || index == line.Length - 1) continue;
                if (TryParseDateTime(line[(index + 1)..].Trim(), out var parsed)) return parsed;
            }
        }
        return null;
    }

    private static bool TryParseDateTime(string value, out DateTimeOffset parsed)
    {
        parsed = default;
        if (value.Length is < 8 or > 60) return false;
        if (!value.Any(char.IsDigit)) return false;
        if (!value.Contains('/') && !value.Contains('-') && !value.Contains('.')) return false;
        if (value.Contains('(')) return false;

        // 系统语言与输出语言未必一致（例如英文系统 + 中文界面包），两种区域性都试一次
        if (!DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var local)
            && !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
            return false;
        if (local.Year is < 2000 or > 2100) return false;

        parsed = local;
        return true;
    }
}
