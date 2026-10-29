using System.Runtime.InteropServices;
using System.Text;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// ImageMagick 引擎：按需下载静态安装包到数据目录，提供路径/就绪/运行/清理。
/// 官方站点 (imagemagick.org/download) 已改为经 GitHub Releases 分发 Windows
/// 二进制，故从官方下载页解析最新版本直链；解析失败兜底固定版本直链。
/// 安装包为 Inno Setup 静默安装（/VERYSILENT）到应用数据目录，免交互。
/// </summary>
public static class MagickService
{
    private static readonly string MagickRoot = Path.Combine(ConfigManager.GetDataDir(), "imagemagick");

    /// <summary>当前架构对应的子目录（不同架构互不覆盖）。</summary>
    private static string ArchDir => Path.Combine(MagickRoot, RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "win-arm64",
        Architecture.X86 => "win-x86",
        _ => "win-x64"
    });

    public static string MagickPath => Path.Combine(ArchDir, "magick.exe");

    public static bool IsMagickReady => File.Exists(MagickPath);

    private static DownloadItem? _downloadItem;
    public static DownloadItem? DownloadItem => _downloadItem;

    public static DownloadItem EnsureMagickViaQueue()
    {
        if (IsMagickReady) return _downloadItem!;

        if (_downloadItem is not null && _downloadItem.State is
            DownloadItemState.Queued or DownloadItemState.Resolving or DownloadItemState.Downloading or DownloadItemState.Processing)
            return _downloadItem;

        Directory.CreateDirectory(ArchDir);

        var postProcessor = new MagickInstallProcessor();
        var arch = RuntimeInformation.ProcessArchitecture;

        Func<CancellationToken, Task<ResolvedDownloadUrl>> urlResolver;
        string? fallbackUrl;

        if (arch == Architecture.X64)
        {
            // x64：GitCode 用户镜像（master 镜像站）zip 主源；官方 GitHub 安装包兜底
            urlResolver = _ => Task.FromResult(new ResolvedDownloadUrl(
                "https://gitcode.com/luolangaga/ImageMagick/releases/download/1/imagemagick.zip",
                "imagemagick.zip", 0));
            fallbackUrl = FallbackUrl(arch);
        }
        else
        {
            // arm64/x86：官方下载页解析最新 static 安装包；固定版本直链兜底
            urlResolver = async ct => await ResolveLatestAsync(arch, ct);
            fallbackUrl = FallbackUrl(arch);
        }

        _downloadItem = DownloadQueueService.EnqueueWithResolver(
            "ImageMagick",
            urlResolver,
            ArchDir,
            postProcessor,
            description: "图片格式转换与压缩引擎 (镜像优先)",
            glyph: "\uE91B",
            fallbackUrl: fallbackUrl);

        return _downloadItem;
    }

    private static string ArchKey(Architecture arch) => arch switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "x64"
    };

    private static string FallbackUrl(Architecture arch)
        => $"https://github.com/ImageMagick/ImageMagick/releases/download/7.1.2-30/ImageMagick-7.1.2-30-Q16-{ArchKey(arch)}-static.exe";

    /// <summary>
    /// 解析官方下载页（imagemagick.org/download）中最新版本的静态安装包直链
    /// （GitHub Releases 资产）。解析失败时兜底返回固定版本直链。
    /// </summary>
    private static async Task<ResolvedDownloadUrl> ResolveLatestAsync(Architecture arch, CancellationToken ct)
    {
        const string downloadPage = "https://imagemagick.org/download/";
        var suffix = $"-Q16-{ArchKey(arch)}-static.exe";

        try
        {
            using var client = ProxyService.CreateClient(TimeSpan.FromSeconds(25));
            var html = await client.GetStringAsync(downloadPage, ct);

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);
            var nodes = doc.DocumentNode.SelectNodes("//a[@href]");
            var candidates = new List<string>();
            if (nodes is not null)
            {
                foreach (var a in nodes)
                {
                    var href = a.GetAttributeValue("href", "");
                    if (href.Contains("/releases/download/", StringComparison.OrdinalIgnoreCase)
                        && href.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        candidates.Add(href);
                }
            }

            if (GetLatestVersionUrl(candidates) is { } url)
                return new ResolvedDownloadUrl(url, Path.GetFileName(url), 0);
        }
        catch (OperationCanceledException) { throw; }
        catch { /* 页面解析失败，走固定版本兜底 */ }

        var fallback = FallbackUrl(arch);
        return new ResolvedDownloadUrl(fallback, Path.GetFileName(fallback), 0);
    }

    /// <summary>从候选直链中取出版本号最大的（releases/download/{version}/...）。</summary>
    private static string? GetLatestVersionUrl(List<string> urls)
    {
        if (urls.Count == 0) return null;
        string? best = null;
        Version? bestVer = null;
        foreach (var url in urls)
        {
            var seg = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var idx = Array.IndexOf(seg, "download");
            if (idx < 0 || idx + 1 >= seg.Length) continue;
            var verStr = seg[idx + 1].Replace("-", ".");
            if (Version.TryParse(verStr, out var v) && (bestVer is null || v > bestVer))
            {
                bestVer = v;
                best = url;
            }
        }
        return best ?? urls[0];
    }

    public static async Task EnsureMagickAsync(IProgress<(int percent, string message)>? progress = null)
    {
        if (IsMagickReady) return;

        var item = EnsureMagickViaQueue();
        var tcs = new TaskCompletionSource<bool>();
        void handler(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DownloadItem.State))
            {
                if (item.State == DownloadItemState.Completed)
                    tcs.TrySetResult(true);
                else if (item.State == DownloadItemState.Failed)
                    tcs.TrySetException(new Exception(item.ErrorMessage ?? "下载失败"));
                else if (item.State == DownloadItemState.Cancelled)
                    tcs.TrySetException(new OperationCanceledException());
            }
        }
        item.PropertyChanged += handler;

        try
        {
            while (!tcs.Task.IsCompleted)
            {
                await Task.WhenAny(tcs.Task, Task.Delay(200));
                var p = item.Progress;
                if (p is not null)
                {
                    var speed = p.SpeedMbps > 0 ? $" {DownloadQueueService.FormatSpeed(p.SpeedMbps)}" : "";
                    var eta = p.EstimatedRemaining.HasValue ? $" 剩余 {DownloadQueueService.FormatTime(p.EstimatedRemaining)}" : "";
                    progress?.Report(((int)p.Percentage, $"正在下载 ImageMagick... {DownloadQueueService.FormatSize(p.BytesReceived)}/{DownloadQueueService.FormatSize(p.TotalBytes)}{speed}{eta}"));
                }
                if (item.State == DownloadItemState.Processing && item.ProcessingStatus is not null)
                    progress?.Report((95, item.ProcessingStatus));
                if (tcs.Task.IsCompleted) break;
            }
            await tcs.Task;
            progress?.Report((100, "ImageMagick 就绪"));
        }
        finally
        {
            item.PropertyChanged -= handler;
        }
    }

    public static async Task<string> RunMagickAsync(string arguments, CancellationToken ct = default)
    {
        if (!IsMagickReady) throw new InvalidOperationException("ImageMagick 未就绪");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = MagickPath,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        proc.Start();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask, proc.WaitForExitAsync(ct));

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (proc.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new Exception($"ImageMagick 退出码 {proc.ExitCode}: {detail}");
        }

        return stdout + stderr;
    }

    public static void DeleteMagick()
    {
        try
        {
            if (Directory.Exists(MagickRoot))
                Directory.Delete(MagickRoot, true);
        }
        catch { }
    }

    public static string GetMagickSize()
    {
        try
        {
            if (!Directory.Exists(MagickRoot)) return "0 B";
            long size = 0;
            foreach (var f in Directory.GetFiles(MagickRoot, "*", SearchOption.AllDirectories))
                try { size += new FileInfo(f).Length; } catch { }
            return DownloadQueueService.FormatSize(size);
        }
        catch { return "未知"; }
    }
}

/// <summary>
/// ImageMagick 安装包后处理器，支持两种发行形态：
///  - zip（GitCode 镜像便携版）：解压到目标目录，若 zip 内含顶层目录则把
///    magick.exe 所在目录的内容平铺到目标根目录；
///  - exe（官方 Inno Setup 安装包）：/VERYSILENT 静默安装到目标目录。
/// </summary>
public sealed class MagickInstallProcessor : IDownloadPostProcessor
{
    public string DisplayName => "解压 ImageMagick";

    public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        if (downloadedFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            statusProgress?.Report("正在解压 ImageMagick...");
            Directory.CreateDirectory(destinationPath);

            await Task.Run(() =>
            {
                var skipped = ZipExtractHelper.ExtractTolerant(
                    downloadedFilePath, destinationPath, statusProgress);
                if (skipped.Count > 0)
                    statusProgress?.Report($"已跳过 {skipped.Count} 个无法解压的文件");
            }, ct);

            FlattenMagickDir(destinationPath, statusProgress);

            if (!File.Exists(Path.Combine(destinationPath, "magick.exe")))
                throw new IOException($"解压后未找到 magick.exe（{destinationPath}）");

            statusProgress?.Report("ImageMagick 就绪");
            try { File.Delete(downloadedFilePath); } catch { }
            return;
        }

        // exe：Inno Setup 安装包 → 静默安装
        statusProgress?.Report("正在静默安装 ImageMagick...");
        Directory.CreateDirectory(destinationPath);

        var psi = new System.Diagnostics.ProcessStartInfo(downloadedFilePath)
        {
            Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=\"{destinationPath}\"",
            UseShellExecute = true
        };

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc is null)
            throw new IOException("无法启动 ImageMagick 安装程序");

        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
            throw new IOException($"ImageMagick 安装程序退出码 {proc.ExitCode}");

        if (!File.Exists(Path.Combine(destinationPath, "magick.exe")))
            throw new IOException($"安装后未找到 magick.exe（{destinationPath}）");

        statusProgress?.Report("ImageMagick 安装完成");
        try { File.Delete(downloadedFilePath); } catch { }
    }

    /// <summary>zip 内含顶层目录时，将含 magick.exe 的子目录内容平铺到目标根目录。</summary>
    private static void FlattenMagickDir(string destinationPath, IProgress<string>? statusProgress)
    {
        if (File.Exists(Path.Combine(destinationPath, "magick.exe"))) return;

        var exe = Directory.GetFiles(destinationPath, "magick.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (exe is null) return;

        var dir = Path.GetDirectoryName(exe)!;
        if (string.Equals(dir, destinationPath, StringComparison.OrdinalIgnoreCase)) return;

        statusProgress?.Report("正在整理目录结构...");
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(dir, f);
            var dest = Path.Combine(destinationPath, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (!File.Exists(dest))
            {
                try { File.Move(f, dest); } catch { }
            }
        }
        foreach (var d in Directory.GetDirectories(destinationPath))
        {
            try { Directory.Delete(d, true); } catch { }
        }
    }
}