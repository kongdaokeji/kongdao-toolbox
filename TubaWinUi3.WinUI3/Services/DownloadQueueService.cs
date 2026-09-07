using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.Toolkit.Uwp.Notifications;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class DownloadQueueService
{
    private const int MaxConcurrentDownloads = 2;
    private const string PartialSuffix = ".tubadl";
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    private static readonly SemaphoreSlim _semaphore = new(MaxConcurrentDownloads);
    private static readonly ObservableCollection<DownloadItem> _queue = [];
    private static readonly Dictionary<string, Task> _activeTasks = [];
    private static int _pendingCount;
    private static DispatcherQueue? _dispatcherQueue;
#pragma warning disable CS0414
    private static bool _dirty;
#pragma warning restore CS0414
    private static readonly object _saveLock = new();

    private static HttpClient CreateHttpClient(TimeSpan? timeout = null)
    {
        var client = ProxyService.CreateClient(timeout ?? TimeSpan.FromMinutes(30));
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-DownloadQueue");
        return client;
    }

    public static void Initialize(DispatcherQueue dq)
    {
        _dispatcherQueue = dq;
        PostProcessorRegistry.RegisterDefaults();
        _ = Task.Run(() => LoadQueue());
    }

    public static ObservableCollection<DownloadItem> Queue => _queue;
    public static event Action? QueueChanged;

    public static int PendingCount => _pendingCount;

    public static DownloadItem Enqueue(
        string displayName,
        string downloadUrl,
        string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null,
        string? glyph = null,
        object? tag = null)
    {
        var item = DownloadItem.CreateDirect(displayName, downloadUrl, destinationPath,
            postProcessor, description, glyph, tag);
        AddAndStart(item);
        return item;
    }

    public static DownloadItem EnqueueWithResolver(
        string displayName,
        Func<CancellationToken, Task<ResolvedDownloadUrl>> urlResolver,
        string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null,
        string? glyph = null,
        object? tag = null,
        string? fallbackUrl = null)
    {
        var item = DownloadItem.CreateWithResolver(displayName, urlResolver, destinationPath,
            postProcessor, description, glyph, tag, fallbackUrl);
        AddAndStart(item);
        return item;
    }

    public static DownloadItem EnqueueMultiFile(
        string displayName,
        Func<CancellationToken, Task<List<ResolvedDownloadUrl>>> multiFileResolver,
        string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null,
        string? glyph = null,
        object? tag = null)
    {
        var item = DownloadItem.CreateMultiFile(displayName, multiFileResolver, destinationPath,
            postProcessor, description, glyph, tag);
        AddAndStart(item);
        return item;
    }

    public static void Pause(string itemId)
    {
        var item = FindItem(itemId);
        if (item is null) return;
        if (item.State is not (DownloadItemState.Downloading or DownloadItemState.Queued)) return;

        item.Cts?.Cancel();
    }

    public static void Resume(string itemId)
    {
        var item = FindItem(itemId);
        if (item is null) return;
        if (item.State != DownloadItemState.Paused) return;

        item.PrepareResume();
        item.SetState(DownloadItemState.Queued);
        StartItemAsync(item);
    }

    public static void Cancel(string itemId)
    {
        var item = FindItem(itemId);
        if (item is null) return;

        item.Cts?.Cancel();
        if (item.State is DownloadItemState.Queued or DownloadItemState.Resolving)
        {
            DispatchState(item, DownloadItemState.Cancelled);
            DecrementPending();
            CleanupPartialFile(item);
            MarkDirty();
        }
        else if (item.State is DownloadItemState.Paused)
        {
            DispatchState(item, DownloadItemState.Cancelled);
            CleanupPartialFile(item);
            MarkDirty();
        }
    }

    public static void Retry(string itemId)
    {
        var item = FindItem(itemId);
        if (item is null) return;
        if (item.State is not (DownloadItemState.Failed or DownloadItemState.Cancelled)) return;

        item.Reset();
        IncrementPending();
        StartItemAsync(item);
    }

    public static void Remove(string itemId)
    {
        var item = FindItem(itemId);
        if (item is null) return;
        if (item.State is DownloadItemState.Downloading or DownloadItemState.Processing or DownloadItemState.Resolving)
        {
            item.Cts?.Cancel();
            return;
        }

        var wasPending = item.State is DownloadItemState.Queued or DownloadItemState.Resolving
            or DownloadItemState.Downloading or DownloadItemState.Processing or DownloadItemState.Paused;
        _queue.Remove(item);
        if (wasPending) DecrementPending();
        CleanupPartialFile(item);
        MarkDirty();
        QueueChanged?.Invoke();
    }

    public static void DeleteFile(string itemId)
    {
        var item = FindItem(itemId);
        if (item is null) return;
        if (item.State is not DownloadItemState.Completed) return;

        try
        {
            var fileName = item.ResolvedFileName ?? SanitizeFileName(item.DisplayName);
            if (!string.IsNullOrEmpty(fileName))
            {
                var filePath = Path.Combine(item.DestinationPath, fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }
        catch { }

        _queue.Remove(item);
        MarkDirty();
        QueueChanged?.Invoke();
    }

    public static void ClearCompleted()
    {
        var toRemove = _queue.Where(i =>
            i.State is DownloadItemState.Completed or DownloadItemState.Failed or DownloadItemState.Cancelled)
            .ToList();
        foreach (var item in toRemove)
            _queue.Remove(item);
        MarkDirty();
        QueueChanged?.Invoke();
    }

    public static void SaveQueue()
    {
        lock (_saveLock)
        {
            try
            {
                var entries = _queue.Select(ToEntry).ToList();
                var json = JsonSerializer.Serialize(entries, _jsonOpts);
                var path = ConfigManager.GetDownloadQueuePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json);
                _dirty = false;
            }
            catch { }
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{(double)bytes / (1L << 30):F2} GB";
        if (bytes >= 1L << 20) return $"{(double)bytes / (1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{(double)bytes / (1L << 10):F1} KB";
        return $"{bytes} B";
    }

    public static string FormatSpeed(double mbps)
    {
        if (mbps >= 1000) return $"{mbps / 1000:F2} Gbps";
        if (mbps >= 1) return $"{mbps:F2} Mbps";
        return $"{mbps * 1000:F0} Kbps";
    }

    public static string FormatTime(TimeSpan? time)
    {
        if (time is null || time.Value.TotalSeconds <= 0) return "--";
        var t = time.Value;
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    private static void LoadQueue()
    {
        try
        {
            var path = ConfigManager.GetDownloadQueuePath();
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<DownloadQueueEntry>>(json);
            if (entries is null) return;

            var items = new List<DownloadItem>();

            foreach (var entry in entries)
            {
                DownloadItem? item = null;
                var postProcessor = PostProcessorRegistry.Find(entry.PostProcessorKey);

                if (!string.IsNullOrEmpty(entry.DirectUrl))
                {
                    item = DownloadItem.CreateDirect(
                        entry.DisplayName, entry.DirectUrl, entry.DestinationPath,
                        postProcessor, entry.Description, entry.Glyph);
                }

                if (item is null) continue;

                item.Id = entry.Id;
                item.ResolvedUrl = entry.ResolvedUrl;
                item.ResolvedFileName = entry.ResolvedFileName;
                item.ResolvedSize = entry.ResolvedSize;

                if (entry.State == DownloadItemState.Paused)
                {
                    item.SetState(DownloadItemState.Paused);
                    item.ResumePosition = entry.BytesReceived;
                    if (entry.TotalBytes > 0)
                        item.SetProgress(new DownloadQueueProgress(entry.BytesReceived, entry.TotalBytes,
                            entry.TotalBytes > 0 ? (double)entry.BytesReceived / entry.TotalBytes * 100 : 0, 0, null));
                    IncrementPending();
                }
                else if (entry.State == DownloadItemState.Completed)
                {
                    if (entry.CompletedAt.HasValue)
                        item.CompletedAt = entry.CompletedAt;
                    item.SetState(DownloadItemState.Completed);
                }
                else if (entry.State == DownloadItemState.Downloading
                    || entry.State == DownloadItemState.Queued
                    || entry.State == DownloadItemState.Resolving)
                {
                    item.SetState(DownloadItemState.Paused);
                    item.ResumePosition = entry.BytesReceived;
                    if (entry.TotalBytes > 0)
                        item.SetProgress(new DownloadQueueProgress(entry.BytesReceived, entry.TotalBytes,
                            entry.TotalBytes > 0 ? (double)entry.BytesReceived / entry.TotalBytes * 100 : 0, 0, null));
                    IncrementPending();
                }
                else
                {
                    item.SetState(entry.State);
                    if (!string.IsNullOrEmpty(entry.ErrorMessage))
                        item.SetErrorMessage(entry.ErrorMessage);
                }

                items.Add(item);
            }

            if (_dispatcherQueue is not null)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    foreach (var item in items)
                        _queue.Add(item);
                    QueueChanged?.Invoke();
                });
            }
            else
            {
                foreach (var item in items)
                    _queue.Add(item);
                QueueChanged?.Invoke();
            }
        }
        catch { }
    }

    private static void MarkDirty()
    {
        _dirty = true;
        _dispatcherQueue?.TryEnqueue(SaveQueue);
    }

    private static DownloadQueueEntry ToEntry(DownloadItem item)
    {
        return new DownloadQueueEntry
        {
            Id = item.Id,
            DisplayName = item.DisplayName,
            Description = item.Description,
            Glyph = item.Glyph,
            DestinationPath = item.DestinationPath,
            DirectUrl = item.DirectUrl,
            State = item.State,
            ResolvedUrl = item.ResolvedUrl,
            ResolvedFileName = item.ResolvedFileName,
            ResolvedSize = item.ResolvedSize,
            BytesReceived = item.Progress?.BytesReceived ?? item.ResumePosition,
            TotalBytes = item.Progress?.TotalBytes ?? 0,
            PostProcessorKey = PostProcessorRegistry.GetKey(item.PostProcessor),
            ErrorMessage = item.ErrorMessage,
            CompletedAt = item.CompletedAt
        };
    }

    private static void AddAndStart(DownloadItem item)
    {
        item.Cts = new CancellationTokenSource();
        _queue.Insert(0, item);
        IncrementPending();
        MarkDirty();
        StartItemAsync(item);

        ShowToast("已加入下载队列", $"\"{item.DisplayName}\" 已开始下载");
    }

    private static async void StartItemAsync(DownloadItem item)
    {
        try
        {
            await _semaphore.WaitAsync(item.Cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            OnDownloadPaused(item);
            return;
        }

        if (item.State is DownloadItemState.Cancelled)
        {
            _semaphore.Release();
            return;
        }

        var task = ProcessItemAsync(item);
        lock (_activeTasks)
            _activeTasks[item.Id] = task;
    }

    private static void OnDownloadPaused(DownloadItem item)
    {
        if (item.State is not (DownloadItemState.Downloading or DownloadItemState.Queued or DownloadItemState.Resolving))
            return;

        var pos = item.ResumePosition;
        if (item.Progress is not null && item.Progress.BytesReceived > pos)
            pos = item.Progress.BytesReceived;
        item.ResumePosition = pos;
        DispatchState(item, DownloadItemState.Paused);
        DecrementPending();
        MarkDirty();
    }

    private static async Task ProcessItemAsync(DownloadItem item)
    {
        var ct = item.Cts?.Token ?? CancellationToken.None;
        try
        {
            if (item.MultiFileResolver is not null)
            {
                await ProcessMultiFileAsync(item, ct);
            }
            else
            {
                var needsResolve = item.ResolvedUrl is null;
                if (needsResolve)
                {
                    DispatchState(item, DownloadItemState.Resolving);
                    var resolved = await ResolveUrlAsync(item, ct);
                    item.ResolvedUrl = resolved.Url;
                    item.ResolvedFileName = resolved.FileName;
                    item.ResolvedSize = resolved.Size;
                    MarkDirty();
                }

                ct.ThrowIfCancellationRequested();

                if (item.State != DownloadItemState.Downloading)
                    DispatchState(item, DownloadItemState.Downloading);
                var downloadedFile = await DownloadFileWithFallbackAsync(item, ct);

                ct.ThrowIfCancellationRequested();

                await RunPostProcessorAsync(item, downloadedFile, ct);
            }

            DispatchCompleted(item);
        }
        catch (OperationCanceledException)
        {
            OnDownloadPaused(item);
        }
        catch (Exception ex)
        {
            var errorMsg = ex.InnerException?.Message ?? ex.Message;
            DispatchError(item, errorMsg);
            DispatchState(item, DownloadItemState.Failed);
            DecrementPending();
            MarkDirty();

            ShowToast("下载失败", $"\"{item.DisplayName}\" 下载失败：{errorMsg}");
        }
        finally
        {
            _semaphore.Release();
            lock (_activeTasks)
                _activeTasks.Remove(item.Id);
            QueueChanged?.Invoke();
        }
    }

    private static async Task ProcessMultiFileAsync(DownloadItem item, CancellationToken ct)
    {
        DispatchState(item, DownloadItemState.Resolving);
        var files = await item.MultiFileResolver!(ct);

        if (files.Count == 0)
        {
            if (item.PostProcessor is not null)
            {
                DispatchState(item, DownloadItemState.Processing);
                DispatchProcessingStatus(item, item.PostProcessor.DisplayName);
                var progress = new Progress<string>(status => DispatchProcessingStatus(item, status));
                await item.PostProcessor.ExecuteAsync(item.DestinationPath, item.DestinationPath, progress, ct);
            }
            return;
        }

        var totalFiles = files.Count;
        var completedFiles = 0;
        long totalBytes = 0;
        long downloadedBytes = 0;

        DispatchState(item, DownloadItemState.Downloading);

        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];

            Directory.CreateDirectory(item.DestinationPath);

            var fileName = file.FileName;
            var localPath = Path.Combine(item.DestinationPath, fileName);
            var partialPath = localPath + PartialSuffix;
            var localDir = Path.GetDirectoryName(localPath);
            if (localDir is not null) Directory.CreateDirectory(localDir);

            using var client = CreateHttpClient(TimeSpan.FromMinutes(5));
            var response = await client.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var fileTotal = response.Content.Headers.ContentLength ?? file.Size;
            totalBytes += fileTotal;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var fs = new FileStream(partialPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);

            var buffer = new byte[81920];
            long fileBytesRead = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var read = await stream.ReadAsync(buffer, ct);
                if (read == 0) break;
                await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                fileBytesRead += read;
                downloadedBytes += read;

                completedFiles = i;
                var overallPct = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100 : 0;
                DispatchProgress(item, new DownloadQueueProgress(downloadedBytes, totalBytes > 0 ? totalBytes : downloadedBytes, overallPct, 0, null));
            }

            await fs.FlushAsync(ct);
            fs.Close();

            if (File.Exists(localPath))
                File.Delete(localPath);
            File.Move(partialPath, localPath);

            completedFiles = i + 1;
            DispatchProgress(item, new DownloadQueueProgress(downloadedBytes, totalBytes > 0 ? totalBytes : downloadedBytes, completedFiles * 100.0 / totalFiles, 0, null));
        }

        DispatchProgress(item, new DownloadQueueProgress(downloadedBytes, downloadedBytes, 100, 0, TimeSpan.Zero));

        ct.ThrowIfCancellationRequested();

        if (item.PostProcessor is not null)
        {
            DispatchState(item, DownloadItemState.Processing);
            DispatchProcessingStatus(item, item.PostProcessor.DisplayName);
            var progress = new Progress<string>(status => DispatchProcessingStatus(item, status));
            await item.PostProcessor.ExecuteAsync(item.DestinationPath, item.DestinationPath, progress, ct);
        }
    }

    private static async Task RunPostProcessorAsync(DownloadItem item, string downloadedFile, CancellationToken ct)
    {
        if (item.PostProcessor is null) return;
        DispatchState(item, DownloadItemState.Processing);
        DispatchProcessingStatus(item, item.PostProcessor.DisplayName);
        var progress = new Progress<string>(status => DispatchProcessingStatus(item, status));
        await item.PostProcessor.ExecuteAsync(downloadedFile, item.DestinationPath, progress, ct);
    }

    /// <summary>
    /// 优先使用主下载源（GitCode），失败时先自动重下一次，
    /// 仍失败则切换备用源（GitHub）重试，并校验 zip 完整性后再交给解压。
    /// </summary>
    private static async Task<string> DownloadFileWithFallbackAsync(DownloadItem item, CancellationToken ct)
    {
        var alternate = !string.IsNullOrEmpty(item.AlternateUrl) &&
                        !string.Equals(item.AlternateUrl, item.ResolvedUrl, StringComparison.OrdinalIgnoreCase)
            ? item.AlternateUrl
            : null;

        // 第 1 次：主源（默认 GitCode）
        try
        {
            return await DownloadAndValidateAsync(item, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception primaryEx)
        {
            // 第 2 次：主源重试一次（网络波动 / 下载到损坏文件常见）
            DispatchProcessingStatus(item, "下载文件校验失败，正在重新下载...");
            DeleteDownloadedQuiet(item);
            try
            {
                return await DownloadAndValidateAsync(item, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception retryEx) when (alternate is not null)
            {
                // 第 3 次：切换备用源（GitHub）
                DispatchProcessingStatus(item, "GitCode 下载失败，正在切换 GitHub 重试...");
                DeleteDownloadedQuiet(item);
                item.ResolvedUrl = alternate;
                item.ResumePosition = 0;
                item.SupportsResume = false;
                MarkDirty();
                try
                {
                    return await DownloadAndValidateAsync(item, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception altEx)
                {
                    throw new InvalidOperationException(
                        $"主源下载失败：{primaryEx.InnerException?.Message ?? primaryEx.Message}；重试失败：{retryEx.InnerException?.Message ?? retryEx.Message}；备用源失败：{altEx.InnerException?.Message ?? altEx.Message}",
                        altEx);
                }
            }
        }
    }

    private static async Task<string> DownloadAndValidateAsync(DownloadItem item, CancellationToken ct)
    {
        var file = await DownloadFileAsync(item, ct);
        ValidateDownloadedFile(file);
        return file;
    }

    /// <summary>
    /// 若是 zip，校验压缩包完整性（遍历并打开每个条目，验证本地文件头）。
    /// 损坏则删除并抛异常，触发自动重下 / 切换源。
    /// </summary>
    private static void ValidateDownloadedFile(string filePath)
    {
        if (!filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return;
        if (!File.Exists(filePath)) return;

        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(filePath);
            foreach (var entry in archive.Entries)
            {
                using var s = entry.Open();
            }
        }
        catch (Exception ex)
        {
            try { File.Delete(filePath); } catch { }
            throw new InvalidDataException($"下载的压缩包已损坏（{ex.Message}）", ex);
        }
    }

    private static void DeleteDownloadedQuiet(DownloadItem item)
    {
        try
        {
            var fileName = item.ResolvedFileName ?? SanitizeFileName(item.DisplayName);
            var finalPath = Path.Combine(item.DestinationPath, fileName);
            if (File.Exists(finalPath)) File.Delete(finalPath);
        }
        catch { }
        DeletePartialQuiet(item);
    }

    private static void DeletePartialQuiet(DownloadItem item)
    {
        try
        {
            var fileName = item.ResolvedFileName ?? SanitizeFileName(item.DisplayName);
            var partialPath = Path.Combine(item.DestinationPath, fileName + PartialSuffix);
            if (File.Exists(partialPath)) File.Delete(partialPath);
        }
        catch { }
    }

    private static void DispatchState(DownloadItem item, DownloadItemState state)
    {
        if (_dispatcherQueue is not null)
            _dispatcherQueue.TryEnqueue(() => item.SetState(state));
        else
            item.SetState(state);
    }

    private static void DispatchProgress(DownloadItem item, DownloadQueueProgress progress)
    {
        if (_dispatcherQueue is not null)
            _dispatcherQueue.TryEnqueue(() => item.SetProgress(progress));
        else
            item.SetProgress(progress);
    }

    private static void DispatchProcessingStatus(DownloadItem item, string status)
    {
        if (_dispatcherQueue is not null)
            _dispatcherQueue.TryEnqueue(() => item.SetProcessingStatus(status));
        else
            item.SetProcessingStatus(status);
    }

    private static void DispatchCompleted(DownloadItem item)
    {
        if (_dispatcherQueue is not null)
            _dispatcherQueue.TryEnqueue(() =>
            {
                item.SetCompleted();
                DecrementPending();
                MarkDirty();

                ShowToast("下载完成", $"\"{item.DisplayName}\" 已下载完成");
            });
        else
        {
            item.SetCompleted();
            DecrementPending();
            MarkDirty();

            ShowToast("下载完成", $"\"{item.DisplayName}\" 已下载完成");
        }
    }

    private static void DispatchError(DownloadItem item, string message)
    {
        if (_dispatcherQueue is not null)
            _dispatcherQueue.TryEnqueue(() => item.SetErrorMessage(message));
        else
            item.SetErrorMessage(message);
    }

    private static async Task<ResolvedDownloadUrl> ResolveUrlAsync(DownloadItem item, CancellationToken ct)
    {
        if (item.DirectUrl is not null)
        {
            var fileName = Path.GetFileName(new Uri(item.DirectUrl).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('?'))
                fileName = SanitizeFileName(item.DisplayName);
            return new ResolvedDownloadUrl(item.DirectUrl, fileName);
        }

        if (item.UrlResolver is not null)
            return await item.UrlResolver(ct);

        throw new InvalidOperationException("No download URL or resolver provided");
    }

    private static async Task<string> DownloadFileAsync(DownloadItem item, CancellationToken ct)
    {
        var url = item.ResolvedUrl!;
        var fileName = item.ResolvedFileName ?? SanitizeFileName(item.DisplayName);
        Directory.CreateDirectory(item.DestinationPath);

        var partialPath = Path.Combine(item.DestinationPath, fileName + PartialSuffix);
        var finalPath = Path.Combine(item.DestinationPath, fileName);

        long existingBytes = 0;
        if (File.Exists(partialPath))
            existingBytes = new FileInfo(partialPath).Length;

        if (File.Exists(finalPath) && existingBytes == 0)
            File.Delete(finalPath);

        using var downloadClient = CreateHttpClient();

        if (existingBytes > 0)
        {
            var rangeRequest = new HttpRequestMessage(HttpMethod.Get, url);
            rangeRequest.Headers.Range = new RangeHeaderValue(existingBytes, null);
            var rangeResponse = await downloadClient.SendAsync(rangeRequest, HttpCompletionOption.ResponseHeadersRead, ct);

            if (rangeResponse.StatusCode == System.Net.HttpStatusCode.PartialContent)
            {
                item.SupportsResume = true;
                rangeResponse.EnsureSuccessStatusCode();
                var result = await WriteDownloadStreamAsync(item, rangeResponse, partialPath, finalPath, existingBytes, item.ResolvedSize, ct);
                rangeResponse.Dispose();
                return result;
            }

            rangeResponse.Dispose();
            try { File.Delete(partialPath); } catch { }
            existingBytes = 0;
            item.ResumePosition = 0;
            item.SupportsResume = false;
        }
        else
        {
            item.SupportsResume = false;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await downloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var file = await WriteDownloadStreamAsync(item, response, partialPath, finalPath, 0, item.ResolvedSize, ct);
        response.Dispose();
        return file;
    }

    private static async Task<string> WriteDownloadStreamAsync(
        DownloadItem item, HttpResponseMessage response,
        string partialPath, string finalPath,
        long existingBytes, long knownSize, CancellationToken ct)
    {
        var totalFromHeader = response.Content.Headers.ContentLength ?? 0;
        var totalBytes = response.StatusCode == System.Net.HttpStatusCode.PartialContent
            ? existingBytes + totalFromHeader
            : totalFromHeader;
        if (totalBytes <= 0)
            totalBytes = knownSize > 0 ? knownSize : 0;

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var fs = new FileStream(partialPath,
            existingBytes > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);

        var buffer = new byte[81920];
        long bytesRead = existingBytes;
        var sw = Stopwatch.StartNew();
        var lastReport = sw.Elapsed;
        long lastBytes = bytesRead;

        if (existingBytes > 0 && totalBytes > 0)
        {
            var initPct = (double)existingBytes / totalBytes * 100;
            DispatchProgress(item, new DownloadQueueProgress(existingBytes, totalBytes, initPct, 0, null));
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;

            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
            bytesRead += read;

            var now = sw.Elapsed;
            if (now - lastReport > TimeSpan.FromMilliseconds(300))
            {
                var chunkBytes = bytesRead - lastBytes;
                var chunkTime = (now - lastReport).TotalSeconds;
                var speedMbps = chunkBytes / Math.Max(chunkTime, 0.001) * 8 / 1_000_000;
                var percentage = totalBytes > 0 ? (double)bytesRead / totalBytes * 100 : 0;
                var remaining = totalBytes > 0 && speedMbps > 0
                    ? TimeSpan.FromSeconds((totalBytes - bytesRead) / Math.Max(speedMbps * 1_000_000 / 8, 1))
                    : (TimeSpan?)null;

                item.ResumePosition = bytesRead;
                DispatchProgress(item, new DownloadQueueProgress(bytesRead, totalBytes, percentage, speedMbps, remaining));
                lastReport = now;
                lastBytes = bytesRead;
            }
        }

        await fs.FlushAsync(ct);
        fs.Close();

        if (File.Exists(finalPath))
            File.Delete(finalPath);
        File.Move(partialPath, finalPath);

        item.ResumePosition = 0;
        DispatchProgress(item, new DownloadQueueProgress(bytesRead, totalBytes > 0 ? totalBytes : bytesRead, 100, 0, TimeSpan.Zero));
        return finalPath;
    }

    private static void CleanupPartialFile(DownloadItem item)
    {
        if (item.State is DownloadItemState.Cancelled or DownloadItemState.Failed)
        {
            var fileName = item.ResolvedFileName ?? SanitizeFileName(item.DisplayName);
            if (string.IsNullOrEmpty(fileName)) return;
            var partialPath = Path.Combine(item.DestinationPath, fileName + PartialSuffix);
            if (File.Exists(partialPath))
            {
                try { File.Delete(partialPath); } catch { }
            }
        }
    }

    private static DownloadItem? FindItem(string itemId)
        => _queue.FirstOrDefault(i => i.Id == itemId);

    private static void ShowToast(string title, string message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
        }
        catch
        {
            // Toast notifications may throw ArgumentException ("Value does not
            // fall within the expected range") in unpackaged mode when no AUMID
            // is registered. Swallow so the download flow is not broken.
        }
    }

    private static void IncrementPending()
    {
        Interlocked.Increment(ref _pendingCount);
        QueueChanged?.Invoke();
    }

    private static void DecrementPending()
    {
        Interlocked.Decrement(ref _pendingCount);
        QueueChanged?.Invoke();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
            if (!invalid.Contains(c)) result.Append(c);
        return result.Length == 0 ? "download" : result.ToString();
    }
}
