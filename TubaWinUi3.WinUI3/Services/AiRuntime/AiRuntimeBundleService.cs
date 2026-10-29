using System.ComponentModel;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services.AiRuntime;

/// <summary>
/// 「AI 运行库」的下载 / 安装 / 就绪判定：
/// AI 依赖的运行时 DLL 不随安装包分发，首次用到 AI 工具（本地AI试炼场）时
/// 从 NuGet 官方源下载对应 .nupkg（整包即 zip），解包提取需要的 DLL 到数据目录，
/// 再由 <see cref="AiRuntimeLoader"/> 动态加载。下载走全局下载队列（断点续传 / 可暂停 / 统一代理）。
/// </summary>
public static class AiRuntimeBundleService
{
    /// <summary>当前进程架构对应的运行库 RID（null = 不支持的架构）。</summary>
    public static string? Rid => AiRuntimeBundle.GetRid();

    /// <summary>数据目录下的运行库根目录（&lt;DataDir&gt;\ai-runtime\&lt;rid&gt;）。</summary>
    private static string RuntimeDir
    {
        get
        {
            var rid = Rid ?? "unknown";
            return Path.Combine(ConfigManager.GetDataDir(), "ai-runtime", rid);
        }
    }

    /// <summary>nupkg 下载落盘目录（运行库根目录的 packages 子目录）。</summary>
    private static string PackagesDir => Path.Combine(RuntimeDir, "packages");

    /// <summary>
    /// 运行库就绪时对应的目录；未就绪返回 null。
    /// 优先使用应用目录自带的一份（-p:BundleAiRuntime=true 的本地开发构建 / 手动放置），
    /// 否则使用数据目录里已安装且契约版本匹配的一份。
    /// </summary>
    public static string? ResolveRuntimeDir()
    {
        var rid = Rid;
        if (rid is null) return null;

        var appDir = AppContext.BaseDirectory;
        if (AiRuntimeBundle.HasAllRequiredFiles(appDir, rid)) return appDir;

        var dir = RuntimeDir;
        if (AiRuntimeBundle.HasAllRequiredFiles(dir, rid) &&
            AiRuntimeBundle.ReadInstalledContractVersion(dir) == AiRuntimeBundle.ContractVersion)
            return dir;

        return null;
    }

    /// <summary>是否已就绪（应用目录自带或数据目录已安装）。</summary>
    public static bool IsReady => ResolveRuntimeDir() is not null;

    /// <summary>最近一次安装项（供再次打开对话框时复用进度）。</summary>
    public static DownloadItem? ActiveInstallItem { get; private set; }

    /// <summary>下载体积提示（对话框文案用）：x64/arm64 约 75MB，x86 约 8MB。</summary>
    public static string GetSizeHint()
    {
        var rid = Rid;
        if (rid is null) return "数十 MB";
        return AiRuntimeBundle.SupportsLocalInference(rid) ? "约 75 MB" : "约 8 MB";
    }

    /// <summary>
    /// 确保运行库已安装（未装则入队下载 + 解包）。就绪返回；失败抛异常（含可读原因）。
    /// 进度约定：percent &lt; 0 表示当前阶段无法给出百分比（解析中 / 解包中）。
    /// </summary>
    public static async Task EnsureInstalledAsync(
        IProgress<(int Percent, string Message)>? progress, CancellationToken ct)
    {
        if (IsReady) return;
        var rid = Rid ?? throw new InvalidOperationException("当前处理器架构不支持 AI 运行库。");

        var item = ActiveInstallItem;
        if (item is null || item.State is DownloadItemState.Completed or DownloadItemState.Failed or DownloadItemState.Cancelled)
        {
            item = DownloadQueueService.EnqueueMultiFile(
                AiRuntimeBundle.DisplayName,
                ResolveDownloadUrlsAsync,
                PackagesDir,
                new AiRuntimeNupkgProcessor(rid),
                description: "本地推理运行组件（NuGet 官方源）",
                glyph: AiRuntimeBundle.Glyph);
            ActiveInstallItem = item;
        }

        // 等待完成：状态变化事件 + 200ms 轮询进度（与 OfficeCliService.EnsureOfficeCliAsync 同模式）
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void handler(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DownloadItem.State)) return;
            switch (item.State)
            {
                case DownloadItemState.Completed:
                    tcs.TrySetResult(true);
                    break;
                case DownloadItemState.Failed:
                    tcs.TrySetException(new InvalidOperationException(item.ErrorMessage ?? "AI 运行库下载失败"));
                    break;
                case DownloadItemState.Cancelled:
                    tcs.TrySetException(new OperationCanceledException());
                    break;
            }
        }

        item.PropertyChanged += handler;
        try
        {
            while (!tcs.Task.IsCompleted)
            {
                ct.ThrowIfCancellationRequested();
                await Task.WhenAny(tcs.Task, Task.Delay(200, ct));

                if (item.State == DownloadItemState.Processing)
                {
                    progress?.Report((-1, item.ProcessingStatus ?? "正在解包 AI 运行库..."));
                }
                else if (item.State == DownloadItemState.Resolving)
                {
                    progress?.Report((-1, "正在解析下载地址..."));
                }
                else if (item.Progress is { } p)
                {
                    var speed = p.SpeedMbps > 0 ? $" · {DownloadQueueService.FormatSpeed(p.SpeedMbps)}" : "";
                    var eta = p.EstimatedRemaining.HasValue
                        ? $" · 剩余 {DownloadQueueService.FormatTime(p.EstimatedRemaining)}"
                        : "";
                    progress?.Report(((int)p.Percentage,
                        $"正在下载 AI 运行库 {DownloadQueueService.FormatSize(p.BytesReceived)} / " +
                        $"{DownloadQueueService.FormatSize(p.TotalBytes)}{speed}{eta}（可在下载队列查看）"));
                }
            }
            await tcs.Task;
        }
        finally
        {
            item.PropertyChanged -= handler;
        }

        if (!IsReady)
            throw new InvalidOperationException("AI 运行库安装未完成，请重试。");
        progress?.Report((100, "AI 运行库已就绪"));
    }

    /// <summary>
    /// 构建下载清单：每个 NuGet 包 → 官方 flatcontainer 主源 + globalcdn 备用源。
    /// 用 HEAD 取文件大小（下载队列据此跳过已完成文件）；两源都探测失败时先给主源，
    /// 真正的失败留给下载阶段（队列自带重试并会重新调用本解析器）。
    /// </summary>
    private static async Task<List<ResolvedDownloadUrl>> ResolveDownloadUrlsAsync(CancellationToken ct)
    {
        var rid = Rid ?? throw new InvalidOperationException("当前处理器架构不支持 AI 运行库。");
        var list = new List<ResolvedDownloadUrl>();

        foreach (var spec in AiRuntimeBundle.GetPackages(rid))
        {
            ct.ThrowIfCancellationRequested();
            var id = spec.Id.ToLowerInvariant();
            var fileName = $"{id}.{spec.Version}.nupkg";
            var primary = $"https://api.nuget.org/v3-flatcontainer/{id}/{spec.Version}/{fileName}";
            var fallback = $"https://globalcdn.nuget.org/packages/{fileName}";

            var (url, size) = await ProbeAsync(primary, fallback, ct);
            list.Add(new ResolvedDownloadUrl(url, fileName, size));
        }

        return list;
    }

    private static async Task<(string Url, long Size)> ProbeAsync(string primary, string fallback, CancellationToken ct)
    {
        foreach (var url in new[] { primary, fallback })
        {
            try
            {
                using var client = ProxyService.CreateClient(TimeSpan.FromSeconds(15));
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.IsSuccessStatusCode)
                    return (url, response.Content.Headers.ContentLength ?? 0);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // 换下一个源
            }
        }
        return (primary, 0);
    }

    /// <summary>
    /// 解包后处理器：把 packages 目录里的 .nupkg 逐个按清单规则提取到运行库根目录，
    /// 校验必备文件后写 manifest（契约版本），全部成功才删除 nupkg（失败保留以便续传/跳过）。
    /// 参数化处理器不注册进 PostProcessorRegistry（与 CommunityToolInstallProcessor 同模式）。
    /// </summary>
    private sealed class AiRuntimeNupkgProcessor(string rid) : IDownloadPostProcessor
    {
        public string DisplayName => "解包 AI 运行库";

        public Task ExecuteAsync(string downloadedFilePath, string destinationPath,
            IProgress<string>? statusProgress, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var runtimeDir = Directory.GetParent(destinationPath)?.FullName
                    ?? throw new InvalidOperationException("AI 运行库目录结构异常。");
                var packages = AiRuntimeBundle.GetPackages(rid);
                var extracted = new List<string>();

                foreach (var file in Directory.GetFiles(destinationPath, "*.nupkg"))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileNameWithoutExtension(file);
                    var spec = packages.FirstOrDefault(p =>
                        name.Equals($"{p.Id}.{p.Version}", StringComparison.OrdinalIgnoreCase));
                    if (spec is null)
                    {
                        statusProgress?.Report($"跳过未知包 {name}");
                        continue;
                    }

                    statusProgress?.Report($"正在解包 {spec.Id}...");
                    extracted.AddRange(AiRuntimeBundle.ExtractNupkg(file, spec, rid, runtimeDir, statusProgress));
                }

                foreach (var required in AiRuntimeBundle.GetRequiredFiles(rid))
                {
                    if (!File.Exists(Path.Combine(runtimeDir, required)))
                        throw new InvalidOperationException($"AI 运行库缺少 {required}，请重试下载。");
                }

                AiRuntimeBundle.WriteManifest(runtimeDir, rid, packages, extracted);
                statusProgress?.Report("AI 运行库安装完成");

                // 安装成功后清理 nupkg（约 70MB）；失败时保留，重试可跳过已完成的文件
                foreach (var file in Directory.GetFiles(destinationPath, "*.nupkg"))
                {
                    try { File.Delete(file); } catch { }
                }
            }, ct);
        }
    }
}
