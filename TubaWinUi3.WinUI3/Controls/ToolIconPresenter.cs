using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TubaWinUi3.Controls;

/// <summary>Keep a usable glyph until an image has actually decoded, including failed SVGs.</summary>
public sealed class ToolIconPresenter : UserControl
{
    private readonly FontIcon _fallback = new() { FontFamily = new FontFamily("Segoe Fluent Icons") };
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private int _generation;
    private bool _loaded;
    private bool _refreshQueued;
    private string? _displayedPath;
    private ImageSource? _displayedSource;
    private bool _highContrast;
    private int _displayedRevision = -1;
    // XAML sources are thread-affine: keep a bounded LRU on each UI thread.
    [ThreadStatic] private static Dictionary<string, LinkedListNode<(string Path, BitmapImage Image)>>? _bitmaps;
    [ThreadStatic] private static LinkedList<(string Path, BitmapImage Image)>? _bitmapLru;
    [ThreadStatic] private static int _bitmapRevision;

    public ToolIconPresenter()
    {
        var grid = new Grid();
        grid.Children.Add(_fallback);
        grid.Children.Add(_image);
        Content = grid;
        _image.ImageOpened += (_, _) => _fallback.Visibility = Visibility.Collapsed;
        _image.ImageFailed += (_, _) =>
        {
            _fallback.Visibility = Visibility.Visible;
            if (_displayedPath is not null && _bitmaps is not null &&
                _bitmaps.Remove(_displayedPath, out var failed)) _bitmapLru?.Remove(failed);
            _displayedPath = null; // Allow a subsequent Loaded/refresh to retry.
        };
        ActualThemeChanged += (_, _) => QueueRefresh();
        Loaded += (_, _) => { _loaded = true; QueueRefresh(); };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            ++_generation;
            if (_displayedPath is not null && _image.Source is BitmapImage { PixelWidth: 0 } pending &&
                _bitmaps is not null && _bitmaps.TryGetValue(_displayedPath, out var node) &&
                ReferenceEquals(node.Value.Image, pending))
            {
                _bitmaps.Remove(_displayedPath);
                _bitmapLru?.Remove(node);
            }
            // An image load can be interrupted by virtualization/navigation.
            // Never treat an unfinished request as an already displayed image.
            _displayedPath = null;
            _displayedSource = null;
        };
    }

    public static readonly DependencyProperty IconPathProperty = DependencyProperty.Register(nameof(IconPath), typeof(string), typeof(ToolIconPresenter), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty IconSourceProperty = DependencyProperty.Register(nameof(IconSource), typeof(ImageSource), typeof(ToolIconPresenter), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(ToolIconPresenter), new PropertyMetadata("\uE8B7", Changed));
    public static readonly DependencyProperty GlyphSizeProperty = DependencyProperty.Register(nameof(GlyphSize), typeof(double), typeof(ToolIconPresenter), new PropertyMetadata(28d, Changed));
    public string? IconPath { get => (string?)GetValue(IconPathProperty); set => SetValue(IconPathProperty, value); }
    public ImageSource? IconSource { get => (ImageSource?)GetValue(IconSourceProperty); set => SetValue(IconSourceProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public double GlyphSize { get => (double)GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs _) => ((ToolIconPresenter)sender).QueueRefresh();

    private void QueueRefresh()
    {
        // Bindings arrive separately. Decode only after the realized control has all its inputs.
        if (!_loaded || _refreshQueued) return;
        _refreshQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            _refreshQueued = false;
            if (_loaded) Refresh();
        });
    }

    private void Refresh()
    {
        _fallback.Glyph = string.IsNullOrWhiteSpace(Glyph) ? "\uE8B7" : Glyph;
        _fallback.FontSize = GlyphSize;
        var highContrast = Services.ThemeColors.IsSystemHighContrast();
        var path = string.IsNullOrWhiteSpace(IconPath) ? null : IconPath;
        var source = path is null ? IconSource : null;
        var revision = Services.ToolIconService.CacheRevision;
        // Glyph/size, Loaded and ordinary theme changes must not restart an image load.
        if (_displayedRevision == revision && _highContrast == highContrast && _displayedPath == path && ReferenceEquals(_displayedSource, source)) return;
        _displayedRevision = revision;
        _highContrast = highContrast;
        _displayedPath = path;
        _displayedSource = source;
        var generation = ++_generation;
        _fallback.Visibility = Visibility.Visible;
        _image.Source = null;
        if (highContrast) return;
        try
        {
            // Subscribe before starting a load; a shared SVG might already have fired Opened.
            if (!string.IsNullOrWhiteSpace(IconPath))
            {
                var bitmap = GetBitmap(IconPath);
                _image.Source = bitmap;
                // A cached source has already fired ImageOpened before this card subscribed.
                if (bitmap.PixelWidth > 0) _fallback.Visibility = Visibility.Collapsed;
            }
            else if (IconSource is SvgImageSource svg && svg.UriSource is not null)
            {
                var local = new SvgImageSource();
                local.Opened += (_, _) => { if (generation == _generation) _fallback.Visibility = Visibility.Collapsed; };
                local.OpenFailed += (_, _) => { if (generation == _generation) _fallback.Visibility = Visibility.Visible; };
                _image.Source = local;
                local.UriSource = svg.UriSource;
            }
            else if (IconSource is not null) _image.Source = IconSource;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ToolIcon] {ex.Message}"); }
    }

    private static BitmapImage GetBitmap(string path)
    {
        _bitmaps ??= new(StringComparer.OrdinalIgnoreCase);
        _bitmapLru ??= new();
        var revision = Services.ToolIconService.CacheRevision;
        if (_bitmapRevision != revision)
        {
            _bitmaps.Clear();
            _bitmapLru.Clear();
            _bitmapRevision = revision;
        }
        if (_bitmaps.TryGetValue(path, out var node))
        {
            _bitmapLru.Remove(node);
            _bitmapLru.AddFirst(node);
            return node.Value.Image;
        }
        var bitmap = new BitmapImage
        {
            DecodePixelType = DecodePixelType.Logical,
            DecodePixelWidth = 48,
            DecodePixelHeight = 48,
            UriSource = new Uri(path)
        };
        _bitmaps[path] = _bitmapLru.AddFirst((path, bitmap));
        if (_bitmaps.Count > 256)
        {
            var oldest = _bitmapLru.Last!;
            _bitmaps.Remove(oldest.Value.Path);
            _bitmapLru.RemoveLast();
        }
        return bitmap;
    }
}
