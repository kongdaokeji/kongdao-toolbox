using Microsoft.UI.Xaml.Media.Imaging;

namespace KongdaoToolbox.Services;

public static class BackgroundService
{
    private const string PathKey = "BackgroundImagePath";
    private const string OpacityKey = "BackgroundOpacity";

    /// <summary>
    /// 参与合成的背景图允许的最大解码宽度。
    ///
    /// 窗口再宽也就在 1920 这个量级，4K 壁纸按 3840 全宽解码只是白白多占一份
    /// 显存与纹理带宽（合成器每帧都要用这张纹理）。上限取 1920 正好等于常见
    /// 桌面宽度 —— 视觉效果不变，不会把背景图降糊。
    /// </summary>
    private const int MaxBackgroundDecodeWidth = 1920;

    private static BitmapImage? _cachedImage;
    private static string? _cachedPath;

    public static string? GetBackgroundPath()
    {
        var stored = AppSettings.Get(PathKey);
        return string.IsNullOrWhiteSpace(stored) ? stored : PathResolver.MakeAbsolute(stored);
    }

    public static void SetBackgroundPath(string? path)
    {
        _cachedImage = null;
        _cachedPath = null;
        if (path is not null)
            AppSettings.Set(PathKey, PathResolver.MakeRelative(path));
        else
            AppSettings.Remove(PathKey);
    }

    public static double GetBackgroundOpacity() => AppSettings.GetDouble(OpacityKey, 0.15);

    public static void SetBackgroundOpacity(double opacity) => AppSettings.Set(OpacityKey, opacity);

    public static BitmapImage? LoadBackgroundImage()
    {
        var path = GetBackgroundPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _cachedImage = null;
            _cachedPath = null;
            return null;
        }

        if (string.Equals(path, _cachedPath, StringComparison.OrdinalIgnoreCase) && _cachedImage is not null)
            return _cachedImage;

        try
        {
            // DecodePixelWidth 必须在 UriSource 之前设置：一旦指定了 UriSource，
            // BitmapImage 会立即开始异步解码，之后再改 DecodePixelWidth 不会生效。
            var image = new BitmapImage();
            if (TryGetSourcePixelWidth(path) > MaxBackgroundDecodeWidth)
                image.DecodePixelWidth = MaxBackgroundDecodeWidth;
            image.UriSource = new Uri(path);

            _cachedImage = image;
            _cachedPath = path;
            return _cachedImage;
        }
        catch
        {
            _cachedImage = null;
            _cachedPath = null;
            return null;
        }
    }

    /// <summary>
    /// 读取图片的原始像素宽度，用于判断是否需要限制解码尺寸。
    ///
    /// GDI+ 的 Image.FromStream 只解析文件头，不会解码整幅像素，因此这里几乎无开销；
    /// 文件流用 using 及时释放，不留文件锁。遇到 GDI+ 不认识的格式（如 webp）或任何
    /// 读取异常时返回 0，调用方据此完全不限制解码宽度 —— 宁可多占点显存，也不能让
    /// 背景图加载失败。
    /// </summary>
    private static int TryGetSourcePixelWidth(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var image = System.Drawing.Image.FromStream(
                stream, useEmbeddedColorManagement: false, validateImageData: false);
            return image.Width;
        }
        catch
        {
            return 0;
        }
    }

    public static List<BackgroundImageEntry> GetImportedBackgrounds()
    {
        var dir = ConfigManager.GetBackgroundsDir();
        if (!Directory.Exists(dir))
            return [];

        var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
        var currentPath = GetBackgroundPath();

        return Directory.GetFiles(dir)
            .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(File.GetCreationTimeUtc)
            .Select(f => new BackgroundImageEntry
            {
                Path = f,
                FileName = Path.GetFileName(f),
                IsSelected = string.Equals(f, currentPath, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }

    public static void SelectBackground(string path)
    {
        if (File.Exists(path))
            SetBackgroundPath(path);
    }

    public static void DeleteBackground(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        if (string.Equals(path, GetBackgroundPath(), StringComparison.OrdinalIgnoreCase))
            SetBackgroundPath(null);

        try { File.Delete(path); } catch { }
    }
}

public sealed class BackgroundImageEntry
{
    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool IsSelected { get; set; }
}
