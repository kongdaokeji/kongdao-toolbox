using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Models;
using Windows.Storage;

namespace TubaWinUi3.Services;

/// <summary>
/// 内置游戏的官方 Logo：本地有缓存就直接用，没有才从官方素材地址下载一次并落盘。
/// 拿不到（首次离线、地址失效）返回 null，界面退回图标字形——不报错、不阻塞。
/// 图片是各家厂商的公开素材，只做「标识这是哪个游戏」用，不随仓库分发。
/// </summary>
public static class GameLogoService
{
    /// <summary>测试用：把数据目录指到临时目录。</summary>
    public static string? DataDirOverride { get; set; }

    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient _http = ProxyService.CreateClient(TimeSpan.FromSeconds(20));

    private static string CacheDir
    {
        get
        {
            var dir = Path.Combine(DataDirOverride ?? ConfigManager.GetDataDir(), "GameLogos");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static Task<ImageSource?> GetAsync(GamePreset preset) => GetAsync(preset.Id, preset.LogoUrls);

    public static Task<ImageSource?> GetAsync(string id, IReadOnlyList<string> urls)
    {
        if (string.IsNullOrWhiteSpace(id) || urls.Count == 0) return Task.FromResult<ImageSource?>(null);
        return _loaded.GetOrAdd(id, _ => LoadAsync(id, urls));
    }

    /// <summary>测试用：丢弃进程内缓存，让下一次调用重新走本地缓存 / 下载。</summary>
    public static void ResetMemoryCache() => _loaded.Clear();

    /// <summary>缓存文件名：一个游戏一个文件，扩展名跟随来源（png / svg / jpg）。</summary>
    internal static string CachePath(string cacheDir, string id, string url)
    {
        var extension = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        if (extension.Length is < 2 or > 5) extension = ".img";
        var name = new string(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return Path.Combine(cacheDir, name + extension);
    }

    private static async Task<ImageSource?> LoadAsync(string id, IReadOnlyList<string> urls)
    {
        var dir = CacheDir;

        foreach (var url in urls)
        {
            var path = CachePath(dir, id, url);
            try
            {
                if (!File.Exists(path))
                {
                    var bytes = await _http.GetByteArrayAsync(url);
                    if (bytes.Length == 0) continue;
                    var temp = path + ".part";
                    await File.WriteAllBytesAsync(temp, bytes);
                    File.Move(temp, path, true);
                }

                var source = await CreateSourceAsync(path);
                if (source is not null) return source;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameLogo] {id} ← {url} 失败：{ex.Message}");
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        // 失败不留在缓存里：离线时先看到字形，联网后再打开就能补上
        _loaded.TryRemove(id, out _);
        return null;
    }

    private static async Task<ImageSource?> CreateSourceAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();

        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            var svg = new SvgImageSource();
            await svg.SetSourceAsync(stream);
            return svg;
        }

        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
