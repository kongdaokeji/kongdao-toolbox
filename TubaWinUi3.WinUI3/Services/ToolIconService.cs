using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class ToolIconService
{
    private static string CacheRoot => ConfigManager.GetIconCacheDir();

    private static string BundledCacheRoot => Path.Combine(ToolCatalog.AppDirectory, "IconCache");

    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(90);
    private const long MaxCacheSizeBytes = 50 * 1024 * 1024;
    private const int MaxCacheFiles = 2000;

    private static readonly Dictionary<string, string> ExtensionGlyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        [".bat"] = "\uE756",
        [".cmd"] = "\uE756",
        [".ps1"] = "\uE943",
        [".vbs"] = "\uE943",
        [".msc"] = "\uEC7A",
    };

    private static readonly LruCache<string, string> _memoryCache = new(512);

    public static string? GetIconGlyph(string toolPath)
    {
        var extension = Path.GetExtension(toolPath);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return ExtensionGlyphs.TryGetValue(extension, out var glyph) ? glyph : "\uE8B7";
    }

    public static string? GetCachedIconPath(string toolPath)
    {
        if (!File.Exists(toolPath))
            return null;

        var extension = Path.GetExtension(toolPath);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return null;

        var cacheKey = GetCacheKey(toolPath);

        if (_memoryCache.TryGetValue(cacheKey, out var cachedPath))
            return cachedPath;

        var bundledIconPath = Path.Combine(BundledCacheRoot, $"{cacheKey}.png");
        if (File.Exists(bundledIconPath) && !IsSourceStale(toolPath, bundledIconPath))
        {
            _memoryCache.Set(cacheKey, bundledIconPath);
            return bundledIconPath;
        }

        Directory.CreateDirectory(CacheRoot);
        var iconPath = Path.Combine(CacheRoot, $"{cacheKey}.png");

        if (!File.Exists(iconPath))
            return null;

        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(iconPath);
        if (age >= CacheMaxAge)
        {
            try { File.Delete(iconPath); } catch { }
            _memoryCache.Remove(cacheKey);
            return null;
        }

        if (IsSourceStale(toolPath, iconPath))
        {
            try { File.Delete(iconPath); } catch { }
            _memoryCache.Remove(cacheKey);
            return null;
        }

        _memoryCache.Set(cacheKey, iconPath);
        return iconPath;
    }

    public static async Task<string?> ExtractIconToCacheAsync(string toolPath)
    {
        if (!File.Exists(toolPath))
            return null;

        var extension = Path.GetExtension(toolPath);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return null;

        return await Task.Run(() =>
        {
            Directory.CreateDirectory(CacheRoot);
            var cacheKey = GetCacheKey(toolPath);
            var iconPath = Path.Combine(CacheRoot, $"{cacheKey}.png");

            if (File.Exists(iconPath))
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(iconPath);
                if (age < CacheMaxAge && !IsSourceStale(toolPath, iconPath))
                {
                    _memoryCache.Set(cacheKey, iconPath);
                    return iconPath;
                }

                try { File.Delete(iconPath); } catch { }
                _memoryCache.Remove(cacheKey);
            }

            var bundledIconPath = Path.Combine(BundledCacheRoot, $"{cacheKey}.png");
            if (File.Exists(bundledIconPath) && !IsSourceStale(toolPath, bundledIconPath))
            {
                try
                {
                    File.Copy(bundledIconPath, iconPath, true);
                    _memoryCache.Set(cacheKey, iconPath);
                    return iconPath;
                }
                catch { }
            }

            try
            {
                using var icon = Icon.ExtractAssociatedIcon(toolPath);
                if (icon is null)
                    return null;

                using var bitmap = icon.ToBitmap();
                bitmap.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
                _memoryCache.Set(cacheKey, iconPath);
                return iconPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unable to extract icon for {toolPath}: {ex.Message}");
                return null;
            }
        });
    }

    public static async Task LoadIconsAsync(
        IReadOnlyList<ToolItem> tools,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcher)
    {
        var itemsToLoad = tools
            .Where(t => t.IconPath is null && !string.IsNullOrWhiteSpace(t.Path))
            .Where(t =>
            {
                var ext = System.IO.Path.GetExtension(t.Path);
                return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (itemsToLoad.Count == 0)
            return;

        // 缓存命中检查移入后台线程：每个工具 8~9 次同步文件系统调用
        // （File.Exists / GetLastWriteTimeUtc / CreateDirectory），在 UI 线程
        // 串行执行 142 个 exe 会造成 ~1200 次同步 syscall 卡顿。
        // IconPath 为 INotifyPropertyChanged，绑定自动 marshal 到 UI 线程
        // （与下方提取完成后的赋值模式一致），无需回到 UI 线程重查。
        var needExtract = await Task.Run(() =>
        {
            var extract = new List<ToolItem>();
            foreach (var tool in itemsToLoad)
            {
                var cached = GetCachedIconPath(tool.Path);
                if (cached is not null)
                    tool.IconPath = cached;
                else
                    extract.Add(tool);
            }
            return extract;
        });

        if (needExtract.Count == 0)
            return;

        var semaphore = new SemaphoreSlim(Environment.ProcessorCount >= 4 ? 8 : 4);
        var tasks = needExtract.Select(async tool =>
        {
            await semaphore.WaitAsync();
            try
            {
                var iconPath = await ExtractIconToCacheAsync(tool.Path);
                if (iconPath is not null)
                {
                    if (dispatcher is not null)
                        dispatcher.TryEnqueue(() => tool.IconPath = iconPath);
                    else
                        tool.IconPath = iconPath;
                }
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);
    }

    public static void CleanExpiredCache()
    {
        if (!Directory.Exists(CacheRoot))
            return;

        _memoryCache.Clear();

        var cutoff = DateTime.UtcNow - CacheMaxAge;

        foreach (var file in Directory.EnumerateFiles(CacheRoot, "*.png"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch { }
        }

        CleanOrphanedCache();
        EnforceCacheSizeLimit();
    }

    public static void CleanAllCache()
    {
        _memoryCache.Clear();

        if (!Directory.Exists(CacheRoot))
            return;

        try
        {
            Directory.Delete(CacheRoot, true);
        }
        catch { }
    }

    public static void InvalidateCache(string toolPath)
    {
        var cacheKey = GetCacheKey(toolPath);
        _memoryCache.Remove(cacheKey);

        var iconPath = Path.Combine(CacheRoot, $"{cacheKey}.png");
        try { if (File.Exists(iconPath)) File.Delete(iconPath); } catch { }
    }

    private static bool IsSourceStale(string toolPath, string cachedIconPath)
    {
        try
        {
            var sourceWrite = File.GetLastWriteTimeUtc(toolPath);
            var cacheWrite = File.GetLastWriteTimeUtc(cachedIconPath);
            return sourceWrite > cacheWrite;
        }
        catch
        {
            return false;
        }
    }

    private static void CleanOrphanedCache()
    {
        if (!Directory.Exists(CacheRoot))
            return;

        try
        {
            var validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tools = ToolCatalog.GetAllToolsCached();
            foreach (var tool in tools)
            {
                var ext = Path.GetExtension(tool.Path);
                if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    validKeys.Add(GetCacheKey(tool.Path));
                }
            }

            foreach (var file in Directory.EnumerateFiles(CacheRoot, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!validKeys.Contains(name))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    private static void EnforceCacheSizeLimit()
    {
        if (!Directory.Exists(CacheRoot))
            return;

        try
        {
            var files = new List<FileInfo>();
            foreach (var file in Directory.EnumerateFiles(CacheRoot, "*.png"))
            {
                try { files.Add(new FileInfo(file)); } catch { }
            }

            var totalSize = files.Sum(f => f.Length);
            if (totalSize <= MaxCacheSizeBytes && files.Count <= MaxCacheFiles)
                return;

            var sorted = files
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();

            foreach (var file in sorted)
            {
                if (totalSize <= MaxCacheSizeBytes && files.Count <= MaxCacheFiles)
                    break;

                var key = Path.GetFileNameWithoutExtension(file.FullName);
                _memoryCache.Remove(key);

                try
                {
                    totalSize -= file.Length;
                    files.Remove(file);
                    file.Delete();
                }
                catch { }
            }
        }
        catch { }
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string GetCacheKey(string toolPath)
    {
        var relative = PathResolver.MakeRelative(toolPath);
        return Hash(relative);
    }

        private sealed class LruCache<TKey, TValue> where TKey : notnull
        {
            private readonly int _capacity;
            private readonly Dictionary<TKey, LinkedListNode<LruEntry>> _map;
            private readonly LinkedList<LruEntry> _list;

            public LruCache(int capacity)
            {
                if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
                _capacity = capacity;
                _map = new Dictionary<TKey, LinkedListNode<LruEntry>>(capacity);
                _list = new LinkedList<LruEntry>();
            }

            public bool TryGetValue(TKey key, out TValue? value)
            {
                lock (_map)
                {
                    if (_map.TryGetValue(key, out var node))
                    {
                        _list.Remove(node);
                        _list.AddFirst(node);
                        value = node.Value.Value;
                        return true;
                    }

                    value = default;
                    return false;
                }
            }

            public void Set(TKey key, TValue value)
            {
                lock (_map)
                {
                    if (_map.TryGetValue(key, out var existing))
                    {
                        _list.Remove(existing);
                        existing.Value = new LruEntry(key, value);
                        _list.AddFirst(existing);
                    }
                    else
                    {
                        if (_map.Count >= _capacity)
                        {
                            var lru = _list.Last!;
                            _list.RemoveLast();
                            _map.Remove(lru.Value.Key);
                        }

                        var node = _list.AddFirst(new LruEntry(key, value));
                        _map[key] = node;
                    }
                }
            }

            public void Remove(TKey key)
            {
                lock (_map)
                {
                    if (_map.Remove(key, out var node))
                        _list.Remove(node);
                }
            }

            public void Clear()
            {
                lock (_map)
                {
                    _map.Clear();
                    _list.Clear();
                }
            }

            private readonly struct LruEntry
            {
                public TKey Key { get; }
                public TValue Value { get; }

                public LruEntry(TKey key, TValue value)
                {
                    Key = key;
                    Value = value;
                }
            }
        }
}
