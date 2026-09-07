using Microsoft.UI.Xaml.Media.Imaging;

namespace TubaWinUi3.Services;

public static class BackgroundService
{
    private const string PathKey = "BackgroundImagePath";
    private const string OpacityKey = "BackgroundOpacity";

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
            _cachedImage = new BitmapImage(new Uri(path));
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
