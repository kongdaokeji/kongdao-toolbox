using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using TubaWinUi3.Models;

namespace TubaWinUi3.Pages;

/// <summary>
/// Win32 layered window that renders hardware monitoring widgets on top of a game window.
/// Uses GDI for rendering — compatible with fullscreen exclusive games.
/// Pattern follows AntiMotionSicknessOverlay.
/// </summary>
public sealed class GameOverlayWindow : IDisposable
{
    private static GameOverlayWindow? _instance;

    private IntPtr _hwnd;
    private int _width, _height;
    private bool _disposed;
    private Timer? _topmostTimer;
    private IntPtr _targetHwnd;
    private bool _desktopMode;
    private readonly List<WidgetInstance> _widgets = new();
    private float _bgOpacity = 0.7f;
    private OverlayPosition _position = OverlayPosition.TopLeft;
    // Cached overlay surface (DIB + memory DC), recreated only when the size changes
    private IntPtr _surfaceDib, _surfaceDc, _surfaceOld, _surfaceBits;
    private int _surfaceW, _surfaceH;
    // Last SetWindowPos position — skip the call when nothing moved
    private int _posX, _posY, _posW, _posH;
    // Chart history buffers
    private readonly ConcurrentDictionary<string, CircularBuffer> _chartData = new();

    #region Win32 P/Invoke

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_SHOWNOACTIVATE = 4;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);

    private static void DwmSetWindowAttr(IntPtr hwnd, uint attr, int val)
    {
        DwmSetWindowAttribute(hwnd, attr, ref val, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public POINT pt;
    }

    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint ULW_ALPHA = 0x00000002;
    private const int AC_SRC_ALPHA = 0x01;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint DWMWA_EXCLUDED_FROM_PEEK = 12;

    #endregion

    public static bool IsRunning => _instance != null;
    public static GameOverlayWindow? Instance => _instance;

    public enum OverlayPosition
    {
        TopLeft, TopCenter, TopRight,
        MiddleLeft, Center, MiddleRight,
        BottomLeft, BottomCenter, BottomRight
    }

    public sealed class WidgetInstance
    {
        public OverlayWidgetType Type;
        public int X, Y, Width, Height;
        public int FontSize = 16;
        public string Prefix = "";
        public bool ShowPrefix = true;
        public int Layer;
        public string CurrentText = "--";
        public bool IsChart;
        // Custom content
        public string CustomText = "";
        public string ImagePath = "";
        public uint ColorArgb = 0xFF00A0FF;
        public uint TextColorArgb = 0xFFFFFFFF;
        // Cached image bitmap
        public SKBitmap? CachedImage;
        // Per-widget cached render resources — recreated only when size/font changes,
        // so a frame no longer allocates a DIB + SKBitmap/SKCanvas per widget.
        public IntPtr Dib, DibDC, DibOld, DibBits;
        public int DibW, DibH;
        public SKBitmap? RenderBmp;
        public SKCanvas? RenderCanvas;
        public SKFont? RenderFont;
        public int RenderW, RenderH, RenderFs;
    }

    public sealed class CircularBuffer
    {
        private readonly float[] _data;
        private int _index, _count;
        public int Count => _count;
        public int Capacity => _data.Length;

        public CircularBuffer(int capacity = 60) { _data = new float[capacity]; }

        public void Add(float value)
        {
            _data[_index] = value;
            _index = (_index + 1) % _data.Length;
            if (_count < _data.Length) _count++;
        }

        public float Get(int index) => index < _count ? _data[(_index - _count + index + _data.Length) % _data.Length] : 0;

        public (float min, float max) GetRange()
        {
            if (_count == 0) return (0, 1);
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < _count; i++)
            {
                var v = Get(i);
                if (v < min) min = v;
                if (v > max) max = v;
            }
            return (min, max == min ? min + 1 : max);
        }
    }

    private GameOverlayWindow() { }

    public static GameOverlayWindow ShowOverlay(IntPtr targetHwnd, List<WidgetInstance> widgets,
        float bgOpacity, OverlayPosition position, int width, int height, bool desktopMode = false)
    {
        _instance?.Dispose();

        // Ensure valid dimensions
        width = Math.Clamp(width, 100, 3840);
        height = Math.Clamp(height, 50, 2160);

        var overlay = new GameOverlayWindow
        {
            _targetHwnd = targetHwnd,
            _desktopMode = desktopMode,
            _bgOpacity = Math.Clamp(bgOpacity, 0.1f, 1f), // at least 10% visible
            _position = position,
            _width = width,
            _height = height
        };
        overlay._widgets.AddRange(widgets);
        overlay.CreateOverlayWindow();
        overlay.StartTopmostTimer();
        _instance = overlay;

        System.Diagnostics.Debug.WriteLine($"[GameOverlay] ShowOverlay: {width}x{height}, opacity={overlay._bgOpacity}, hwnd={overlay._hwnd}");
        return overlay;
    }

    public static void CloseOverlay()
    {
        _instance?.Dispose();
        _instance = null;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static WndProcDelegate? _wndProcDelegate; // prevent GC

    private void CreateOverlayWindow()
    {
        _wndProcDelegate = WndProc;

        // Use unique class name to avoid stale registration from crashed runs
        string className = "Tuba_GameOvl_" + Guid.NewGuid().ToString("N")[..8];
        var hInst = Marshal.GetHINSTANCE(typeof(GameOverlayWindow).Module);

        var wndClass = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            lpszClassName = className,
            hInstance = hInst
        };
        var atom = RegisterClassW(ref wndClass);
        if (atom == 0)
        {
            int err = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"[GameOverlay] RegisterClassW FAILED: {err}");
            return;
        }

        // Position: center of screen by default, or relative to target window.
        // In desktop mode the overlay is placed at the configured position over the whole screen.
        int x, y;
        int screenW = GetSystemMetrics(0);
        int screenH = GetSystemMetrics(1);
        if (_desktopMode)
        {
            var (ox, oy) = CalculateOffset(screenW, screenH);
            x = ox;
            y = oy;
        }
        else if (_targetHwnd != IntPtr.Zero && IsWindow(_targetHwnd) && GetWindowRect(_targetHwnd, out var rc))
        {
            var (ox, oy) = CalculateOffset(rc.Right - rc.Left, rc.Bottom - rc.Top);
            x = rc.Left + ox;
            y = rc.Top + oy;
        }
        else
        {
            x = (screenW - _width) / 2;
            y = (screenH - _height) / 2;
        }

        _hwnd = CreateWindowExW(
            WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            className, "", WS_POPUP | WS_VISIBLE,
            x, y, _width, _height,
            IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"[GameOverlay] CreateWindowExW FAILED: {err}");
            return;
        }

        try { DwmSetWindowAttr(_hwnd, DWMWA_EXCLUDED_FROM_PEEK, 1); } catch { }

        // Show window first, then render content into it
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, _width, _height, SWP_SHOWWINDOW);
        RenderFrame();

        System.Diagnostics.Debug.WriteLine($"[GameOverlay] Window created: hwnd={_hwnd}, size={_width}x{_height}, pos=({x},{y})");
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                return (IntPtr)HTTRANSPARENT;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void StartTopmostTimer()
    {
        // Re-assert topmost Z-order for fullscreen games. UpdateData already re-asserts
        // HWND_TOPMOST on every poll tick, so this timer only matters while polling is
        // paused — 1s is enough; 200ms would poke DWM 5x/s for the whole overlay lifetime.
        _topmostTimer = new Timer(_ =>
        {
            if (_hwnd != IntPtr.Zero && IsWindow(_hwnd))
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }, null, 1000, 1000);
    }

    public void UpdateData(MonitorSample sample)
    {
        foreach (var w in _widgets)
        {
            if (w.IsChart)
            {
                var (chartKey, value) = GetChartValue(w.Type, sample);
                if (chartKey != null)
                {
                    var buf = _chartData.GetOrAdd(chartKey, _ => new CircularBuffer(60));
                    if (value >= 0) buf.Add(value);
                }
            }
            else if (w.Type is OverlayWidgetType.CustomText or OverlayWidgetType.CustomImage or OverlayWidgetType.ColorBlock)
            {
                // Static content — no dynamic update needed
            }
            else
            {
                var value = FormatWidgetValue(w.Type, sample);
                w.CurrentText = w.ShowPrefix && !string.IsNullOrEmpty(w.Prefix)
                    ? $"{w.Prefix}{value}"
                    : value;
            }
        }

        if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd)) return;

        // Reposition only when the target window / screen size actually moved — data
        // updates every tick, but poking DWM via SetWindowPos only when needed.
        bool needMove = false;
        int x = 0, y = 0;
        if (_desktopMode)
        {
            // Desktop mode: stay at the configured position over the whole screen
            // (track resolution changes since we don't have a target window rect).
            var (ox, oy) = CalculateOffset(GetSystemMetrics(0), GetSystemMetrics(1));
            x = ox; y = oy; needMove = true;
        }
        else if (_targetHwnd != IntPtr.Zero && IsWindow(_targetHwnd) && GetWindowRect(_targetHwnd, out var rc))
        {
            var (ox, oy) = CalculateOffset(rc.Right - rc.Left, rc.Bottom - rc.Top);
            x = rc.Left + ox; y = rc.Top + oy; needMove = true;
        }

        if (needMove && (x != _posX || y != _posY || _width != _posW || _height != _posH))
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, _width, _height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            _posX = x; _posY = y; _posW = _width; _posH = _height;
        }

        RenderFrame();
    }

    private (int x, int y) CalculateOffset(int screenW, int screenH)
    {
        int m = 10;
        return _position switch
        {
            OverlayPosition.TopLeft => (m, m),
            OverlayPosition.TopCenter => ((screenW - _width) / 2, m),
            OverlayPosition.TopRight => (screenW - _width - m, m),
            OverlayPosition.MiddleLeft => (m, (screenH - _height) / 2),
            OverlayPosition.Center => ((screenW - _width) / 2, (screenH - _height) / 2),
            OverlayPosition.MiddleRight => (screenW - _width - m, (screenH - _height) / 2),
            OverlayPosition.BottomLeft => (m, screenH - _height - m),
            OverlayPosition.BottomCenter => ((screenW - _width) / 2, screenH - _height - m),
            OverlayPosition.BottomRight => (screenW - _width - m, screenH - _height - m),
            _ => (m, m)
        };
    }

    #region Rendering — per-pixel alpha via UpdateLayeredWindow

    private void RenderFrame()
    {
        if (_hwnd == IntPtr.Zero) return;

        var hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero) return;
        try
        {
            if (!EnsureSurface(hdcScreen)) return;

            // Compose the whole frame into a premultiplied 32bpp DIB. The background panel
            // uses _bgOpacity while widgets stay fully opaque — so the 背景透明度 slider
            // only affects the background, not the text/charts on top of it.
            byte bgA = (byte)(_bgOpacity * 255);
            uint r = (uint)(0x1E * bgA / 255), g = (uint)(0x1E * bgA / 255), b = (uint)(0x1E * bgA / 255);
            uint bgPixel = (uint)bgA << 24 | b << 16 | g << 8 | r;
            unsafe { new Span<uint>((void*)_surfaceBits, _width * _height).Fill(bgPixel); }

            // Draw each widget — sort by layer so higher layers render on top
            foreach (var w in _widgets.OrderBy(x => x.Layer))
            {
                if (w.IsChart) DrawChartGdi(_surfaceDc, w);
                else if (w.Type == OverlayWidgetType.ColorBlock) DrawColorBlock(_surfaceDc, w);
                else if (w.Type == OverlayWidgetType.CustomImage) DrawCustomImage(_surfaceDc, w);
                else if (w.Type == OverlayWidgetType.CustomText) DrawTextSkia(_surfaceDc, w, w.CustomText);
                else DrawTextSkia(_surfaceDc, w);
            }

            // Push the whole surface to the layered window with per-pixel alpha
            GetWindowRect(_hwnd, out var rc);
            var ptDst = new POINT { X = rc.Left, Y = rc.Top };
            var ptSrc = new POINT { X = 0, Y = 0 };
            var size = new SIZE { cx = _width, cy = _height };
            var blend = new BLENDFUNCTION
            {
                BlendOp = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };
            UpdateLayeredWindow(_hwnd, hdcScreen, ref ptDst, ref size, _surfaceDc, ref ptSrc, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    /// <summary>Reuses the overlay surface (DIB + memory DC); rebuilt only when the size changes.</summary>
    private bool EnsureSurface(IntPtr hdcScreen)
    {
        if (_surfaceDib != IntPtr.Zero && _surfaceW == _width && _surfaceH == _height)
            return true;

        ReleaseSurface();
        if (_width <= 0 || _height <= 0) return false;

        var bi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = _width,
                biHeight = -_height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };
        _surfaceDib = CreateDIBSection(hdcScreen, ref bi, 0, out var pBits, IntPtr.Zero, 0);
        if (_surfaceDib == IntPtr.Zero) return false;
        _surfaceBits = pBits;
        _surfaceDc = CreateCompatibleDC(hdcScreen);
        if (_surfaceDc == IntPtr.Zero)
        {
            DeleteObject(_surfaceDib);
            _surfaceDib = IntPtr.Zero;
            return false;
        }
        _surfaceOld = SelectObject(_surfaceDc, _surfaceDib);
        _surfaceW = _width;
        _surfaceH = _height;
        return true;
    }

    private void ReleaseSurface()
    {
        if (_surfaceDc != IntPtr.Zero && _surfaceOld != IntPtr.Zero)
            SelectObject(_surfaceDc, _surfaceOld);
        if (_surfaceDib != IntPtr.Zero) DeleteObject(_surfaceDib);
        if (_surfaceDc != IntPtr.Zero) DeleteDC(_surfaceDc);
        _surfaceOld = _surfaceDib = _surfaceDc = _surfaceBits = IntPtr.Zero;
        _surfaceW = _surfaceH = 0;
    }

    /// <summary>Reuses a widget's DIB + DC (shared by text blit / color block / image); rebuilt on size change.</summary>
    private static bool EnsureWidgetSurface(WidgetInstance w, IntPtr hdc)
    {
        if (w.Dib != IntPtr.Zero && w.DibW == w.Width && w.DibH == w.Height)
            return true;

        ReleaseWidgetSurface(w);

        var bi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w.Width,
                biHeight = -w.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };
        w.Dib = CreateDIBSection(hdc, ref bi, 0, out var pBits, IntPtr.Zero, 0);
        if (w.Dib == IntPtr.Zero) return false;
        w.DibBits = pBits;
        w.DibDC = CreateCompatibleDC(hdc);
        if (w.DibDC == IntPtr.Zero)
        {
            DeleteObject(w.Dib);
            w.Dib = IntPtr.Zero;
            return false;
        }
        w.DibOld = SelectObject(w.DibDC, w.Dib);
        w.DibW = w.Width;
        w.DibH = w.Height;
        return true;
    }

    private static void ReleaseWidgetSurface(WidgetInstance w)
    {
        if (w.DibDC != IntPtr.Zero && w.DibOld != IntPtr.Zero)
            SelectObject(w.DibDC, w.DibOld);
        if (w.Dib != IntPtr.Zero) DeleteObject(w.Dib);
        if (w.DibDC != IntPtr.Zero) DeleteDC(w.DibDC);
        w.DibOld = w.Dib = w.DibDC = w.DibBits = IntPtr.Zero;
        w.DibW = w.DibH = 0;
    }

    /// <summary>Reuses a widget's Skia canvas; rebuilt when the size or font size changes.</summary>
    private static SKCanvas EnsureWidgetCanvas(WidgetInstance w, out SKBitmap bmp)
    {
        if (w.RenderBmp is null || w.RenderCanvas is null ||
            w.RenderW != w.Width || w.RenderH != w.Height || w.RenderFs != w.FontSize)
        {
            ReleaseWidgetSkia(w);
            // Same pixel format as the original per-frame bitmaps (charts used explicit
            // Premul; text used the (w, h, isOpaque) ctor) — single shared form for all.
            w.RenderBmp = new SKBitmap(w.Width, w.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            w.RenderCanvas = new SKCanvas(w.RenderBmp);
            w.RenderW = w.Width;
            w.RenderH = w.Height;
            w.RenderFs = w.FontSize;
        }
        bmp = w.RenderBmp;
        return w.RenderCanvas!;
    }

    private static void ReleaseWidgetSkia(WidgetInstance w)
    {
        w.RenderCanvas?.Dispose();
        w.RenderBmp?.Dispose();
        w.RenderFont?.Dispose();
        w.RenderCanvas = null;
        w.RenderBmp = null;
        w.RenderFont = null;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);

    [DllImport("gdi32.dll")]
    private static extern bool MoveToEx(IntPtr hdc, int X, int Y, IntPtr lpPoint);

    [DllImport("gdi32.dll")]
    private static extern bool LineTo(IntPtr hdc, int nXEnd, int nYEnd);

    [DllImport("msimg32.dll")]
    private static extern bool AlphaBlend(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, BLENDFUNCTION blendFunction);

    private void DrawTextSkia(IntPtr hdc, WidgetInstance w, string? textOverride = null)
    {
        if (w.Width <= 0 || w.Height <= 0) return;

        string text = textOverride ?? w.CurrentText;
        if (string.IsNullOrEmpty(text)) return;

        var canvas = EnsureWidgetCanvas(w, out var bmp);

        float fontSize = Math.Max(8, w.FontSize);
        if (w.RenderFont is null || w.RenderFont.Size != fontSize)
        {
            w.RenderFont?.Dispose();
            w.RenderFont = new SKFont(TypefaceBold, fontSize);
        }
        var font = w.RenderFont;

        // Vertical centering: baseline = ascent center within the widget height
        font.GetFontMetrics(out var fm);
        float textY = (w.Height - (fm.Descent - fm.Ascent)) / 2f - fm.Ascent;

        uint tc = w.TextColorArgb;
        byte ta = (byte)((tc >> 24) & 0xFF);
        byte tr = (byte)((tc >> 16) & 0xFF);
        byte tg = (byte)((tc >> 8) & 0xFF);
        byte tb = (byte)(tc & 0xFF);

        // Shadow — auto white or black depending on text luminance
        uint lum = tr * 299u + tg * 587u + tb * 114u;
        byte sa = (byte)(ta * 120 / 255);
        using var shadowPaint = new SKPaint { Color = new SKColor(lum < 100_000 ? (byte)255 : (byte)0, lum < 100_000 ? (byte)255 : (byte)0, lum < 100_000 ? (byte)255 : (byte)0, sa), IsAntialias = true };
        canvas.DrawText(text, 2, textY + 1, font, shadowPaint);

        // Main text — premultiplied ARGB, rendered correctly by SkiaSharp
        using var textPaint = new SKPaint { Color = new SKColor(tr, tg, tb, ta), IsAntialias = true };
        canvas.DrawText(text, 1, textY, font, textPaint);

        canvas.Flush();
        BlitSkiaBitmap(hdc, w, bmp);
    }

    /// <summary>
    /// Renders a solid color block widget with per-pixel alpha so the chosen
    /// ARGB color (e.g. 透明黑) fades the game/desktop behind it instead of
    /// painting an opaque box.
    /// </summary>
    private void DrawColorBlock(IntPtr hdc, WidgetInstance w)
    {
        if (w.Width <= 0 || w.Height <= 0) return;
        if (!EnsureWidgetSurface(w, hdc)) return;

        // Build a premultiplied BGRA DIB and alpha-blend it into the window DC
        uint a = (w.ColorArgb >> 24) & 0xFF;
        uint r = (w.ColorArgb >> 16) & 0xFF;
        uint g = (w.ColorArgb >> 8) & 0xFF;
        uint b = w.ColorArgb & 0xFF;
        uint pixel = (uint)(a << 24 | (b * a / 255) << 16 | (g * a / 255) << 8 | (r * a / 255));
        unsafe { new Span<uint>((void*)w.DibBits, w.DibW * w.DibH).Fill(pixel); }

        var blend = new BLENDFUNCTION
        {
            BlendOp = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AC_SRC_ALPHA
        };
        AlphaBlend(hdc, w.X, w.Y, w.Width, w.Height, w.DibDC, 0, 0, w.Width, w.Height, blend);

        // Draw a border for visibility
        var hPen = CreatePen(0, 1, 0x00FFFFFFu);
        var sel = SelectObject(hdc, hPen);
        MoveToEx(hdc, w.X, w.Y, IntPtr.Zero);
        LineTo(hdc, w.X + w.Width, w.Y);
        LineTo(hdc, w.X + w.Width, w.Y + w.Height);
        LineTo(hdc, w.X, w.Y + w.Height);
        LineTo(hdc, w.X, w.Y);
        SelectObject(hdc, sel);
        DeleteObject(hPen);
    }

    /// <summary>
    /// Draws a custom image widget via SkiaSharp (scaled to widget bounds).
    /// </summary>
    private void DrawCustomImage(IntPtr hdc, WidgetInstance w)
    {
        if (w.Width <= 0 || w.Height <= 0) return;

        // Load lazily and cache
        if (w.CachedImage == null && !string.IsNullOrEmpty(w.ImagePath))
        {
            try
            {
                if (File.Exists(w.ImagePath))
                    w.CachedImage = SKBitmap.Decode(w.ImagePath);
            }
            catch { }
        }

        if (w.CachedImage == null) return;

        var canvas = EnsureWidgetCanvas(w, out var dst);
        // Scale-to-fill (cover) with transparency
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(w.CachedImage, new SKRect(0, 0, w.Width, w.Height));
        canvas.Flush();
        BlitSkiaBitmap(hdc, w, dst);
    }

    // Cached typefaces to avoid per-frame allocation/leak
    private static SKTypeface? _typefaceBold;
    private static SKTypeface? _typefaceNormal;
    private static string _fontFamily = "Microsoft YaHei UI";
    private static SKTypeface TypefaceBold => _typefaceBold ??= SKTypeface.FromFamilyName(_fontFamily, SKFontStyle.Bold);
    private static SKTypeface TypefaceNormal => _typefaceNormal ??= SKTypeface.FromFamilyName(_fontFamily, SKFontStyle.Normal);

    /// <summary>
    /// Changes the font family used by all overlay text and chart labels.
    /// Call from the settings UI when the user picks a different font.
    /// </summary>
    public static void SetFontFamily(string family)
    {
        if (string.IsNullOrWhiteSpace(family)) return;
        _fontFamily = family;
        _typefaceBold = null; // force re-creation on next access
        _typefaceNormal = null;
        // 缓存的 SKFont 持有旧 typeface 引用，字体切换后逐个重建，下一帧生效
        var inst = _instance;
        if (inst != null)
        {
            foreach (var w in inst._widgets)
            {
                w.RenderFont?.Dispose();
                w.RenderFont = null;
            }
        }
    }

    public static string FontFamily => _fontFamily;

    /// <summary>
    /// Renders a chart widget using SkiaSharp (the component library used by LiveCharts2),
    /// then blits the resulting bitmap into the GDI window DC.
    /// </summary>
    private void DrawChartGdi(IntPtr hdc, WidgetInstance w)
    {
        if (w.Width <= 0 || w.Height <= 0) return;

        var chartKey = w.Type switch
        {
            OverlayWidgetType.FpsChart => "fps",
            OverlayWidgetType.CpuTempChart => "cputemp",
            _ => null
        };
        if (chartKey == null || !_chartData.TryGetValue(chartKey, out var buf) || buf.Count < 2) return;

        var (min, max) = buf.GetRange();
        int pad = 6;
        // Title font size follows the widget's FontSize (editable in the property panel),
        // auto-shrunk when the widget is too short; the chart area adapts to it.
        float titleFs = Math.Clamp(w.FontSize, 8, Math.Max(8, (w.Height - 2 * pad - 8) * 0.55f));
        int cx = pad, cy = pad + (int)titleFs + 4, cw = w.Width - pad * 2, ch = w.Height - pad * 2 - (int)titleFs - 4;
        if (cw < 4 || ch < 4) return;

        // Build SKBitmap sized to the widget (reused across frames — the surface is
        // recreated only when the widget size or font size changes)
        var canvas = EnsureWidgetCanvas(w, out var bmp);
        canvas.Clear(SKColors.Transparent);

        // --- Dark rounded background (semi-transparent panel; corners stay transparent) ---
        var bgPaint = new SKPaint { Color = new SKColor(30, 30, 30, 130), IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(0, 0, w.Width, w.Height), 6, 6, bgPaint);
        bgPaint.Dispose();

        // --- Title ---
        // NOTE: DrawText's y is the BASELINE, not the top — draw at `pad + titleFs` so the
        // glyphs don't get clipped above the bitmap edge.
        string title = chartKey == "fps" ? "FPS" : "CPU °C";
        using var titlePaint = new SKPaint
        {
            Color = new SKColor(200, 200, 200, 255),
            IsAntialias = true,
            Typeface = TypefaceBold,
            TextSize = titleFs
        };
        int titleBaseline = pad + (int)titleFs;
        canvas.DrawText(title, pad, titleBaseline, titlePaint);

        // --- Horizontal grid lines ---
        var gridPaint = new SKPaint { Color = new SKColor(60, 60, 60, 120), StrokeWidth = 1 };
        for (int g = 1; g <= 3; g++)
        {
            int gy = cy + ch * g / 4;
            canvas.DrawLine(cx, gy, cx + cw, gy, gridPaint);
        }
        gridPaint.Dispose();

        // --- Build points ---
        int count = Math.Min(buf.Count, cw);
        float xStep = (float)cw / Math.Max(1, count - 1);
        var points = new SKPoint[count];
        for (int i = 0; i < count; i++)
        {
            float sampleVal = buf.Get(buf.Count - count + i);
            float norm = max > min ? (sampleVal - min) / (max - min) : 0.5f;
            points[i] = new SKPoint(
                cx + i * xStep,
                Math.Clamp(cy + ch - norm * ch, cy, cy + ch)
            );
        }

        // --- Gradient area fill under the line ---
        var lineColor = chartKey == "fps"
            ? new SKColor(60, 230, 110)   // green
            : new SKColor(255, 170, 40);  // orange
        using var areaPath = new SKPath();
        areaPath.MoveTo(points[0].X, cy + ch);
        foreach (var p in points) areaPath.LineTo(p);
        areaPath.LineTo(points[^1].X, cy + ch);
        areaPath.Close();

        var fillPaint = new SKPaint { IsAntialias = true };
        fillPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, cy), new SKPoint(0, cy + ch),
            new[] { lineColor.WithAlpha(90), lineColor.WithAlpha(0) },
            new[] { 0f, 1f }, SKShaderTileMode.Clamp);
        canvas.DrawPath(areaPath, fillPaint);
        fillPaint.Dispose();

        // --- Glow line (thicker, dimmer) ---
        using (var glowPaint = new SKPaint
        {
            Color = lineColor.WithAlpha(55),
            StrokeWidth = 4,
            IsAntialias = true,
            IsStroke = true,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round
        })
        {
            using var glowPath = new SKPath();
            glowPath.MoveTo(points[0]);
            for (int i = 1; i < count; i++) glowPath.LineTo(points[i]);
            canvas.DrawPath(glowPath, glowPaint);
        }

        // --- Main line ---
        using (var linePaint = new SKPaint
        {
            Color = lineColor,
            StrokeWidth = 2,
            IsAntialias = true,
            IsStroke = true,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round
        })
        {
            using var linePath = new SKPath();
            linePath.MoveTo(points[0]);
            for (int i = 1; i < count; i++) linePath.LineTo(points[i]);
            canvas.DrawPath(linePath, linePaint);
        }

        // --- Current value dot ---
        using (var dotPaint = new SKPaint { Color = lineColor, IsAntialias = true })
            canvas.DrawCircle(points[^1], 4, dotPaint);
        using (var dotRing = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true
        })
            canvas.DrawCircle(points[^1], 4, dotRing);

        // --- Labels: title value, min/max ---
        using var labelPaint = new SKPaint
        {
            Color = new SKColor(150, 150, 150, 255),
            IsAntialias = true,
            Typeface = TypefaceNormal,
            TextSize = Math.Max(8, titleFs * 0.8f)
        };
        float val = buf.Get(buf.Count - 1);
        float valX = pad + 24;
        canvas.DrawText($"{val:F0}", valX, titleBaseline, titlePaint); // current value beside title

        canvas.DrawText($"{max:F0}", w.Width - 32, cy + 12, labelPaint);
        canvas.DrawText($"{min:F0}", w.Width - 32, cy + ch - 3, labelPaint);

        canvas.Flush();

        // --- Blit SKBitmap into GDI DC at widget position ---
        BlitSkiaBitmap(hdc, w, bmp);
    }

    /// <summary>
    /// Copies an SKBitmap into a GDI memory DC at the given position (widget coordinate),
    /// reusing the widget's cached DIB (sized to the widget) instead of allocating per frame.
    /// </summary>
    private void BlitSkiaBitmap(IntPtr hdcDest, WidgetInstance w, SKBitmap bmp)
    {
        if (bmp.Width <= 0 || bmp.Height <= 0) return;
        if (!EnsureWidgetSurface(w, hdcDest)) return;

        // Copy BGRA premultiplied pixels (bmp is widget-sized)
        var pixels = bmp.Bytes;
        Marshal.Copy(pixels, 0, w.DibBits, Math.Min(pixels.Length, w.DibW * w.DibH * 4));

        // Alpha-blend into destination DC — a plain BitBlt(SRCCOPY) would paste the
        // transparent (premultiplied-black) pixels as opaque black, making the chart
        // background look like a solid black box.
        var blend = new BLENDFUNCTION
        {
            BlendOp = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AC_SRC_ALPHA
        };
        AlphaBlend(hdcDest, w.X, w.Y, bmp.Width, bmp.Height, w.DibDC, 0, 0, bmp.Width, bmp.Height, blend);
    }

    #endregion

    #region Drawing primitives — removed (now using GDI directly in RenderFrame)

    #endregion

    #region Widget drawing — removed (now using GDI directly in RenderFrame)

    #endregion

    #region Widget text formatting

    /// <summary>
    /// Returns the default prefix label for a widget type, e.g. "FPS：", "CPU 温度：".
    /// Used when adding a widget so the overlay reads "FPS：120" instead of "120".
    /// </summary>
    public static string GetDefaultPrefix(OverlayWidgetType type)
    {
        return type switch
        {
            OverlayWidgetType.FpsText => "FPS: ",
            OverlayWidgetType.CpuTempText => "CPU 温度: ",
            OverlayWidgetType.CpuLoadText => "CPU 负载: ",
            OverlayWidgetType.CpuClockText => "CPU 频率: ",
            OverlayWidgetType.CpuPowerText => "CPU 功耗: ",
            OverlayWidgetType.CpuNameText => "CPU: ",
            OverlayWidgetType.GpuTempText => "GPU 温度: ",
            OverlayWidgetType.GpuLoadText => "GPU 负载: ",
            OverlayWidgetType.GpuClockText => "GPU 频率: ",
            OverlayWidgetType.GpuPowerText => "GPU 功耗: ",
            OverlayWidgetType.GpuVramText => "显存: ",
            OverlayWidgetType.GpuNameText => "GPU: ",
            OverlayWidgetType.MemLoadText => "内存负载: ",
            OverlayWidgetType.MemUsedText => "内存使用: ",
            OverlayWidgetType.DiskReadText => "磁盘读取: ",
            OverlayWidgetType.DiskWriteText => "磁盘写入: ",
            OverlayWidgetType.NetUpText => "网络上传: ",
            OverlayWidgetType.NetDownText => "网络下载: ",
            _ => ""
        };
    }

    /// <summary>
    /// Returns just the value portion (no prefix), e.g. "120", "65°C", "3.8 GHz".
    /// </summary>
    private static string FormatWidgetValue(OverlayWidgetType type, MonitorSample s)
    {
        return type switch
        {
            OverlayWidgetType.FpsText => s.Fps >= 0 ? $"{s.Fps:F0} FPS" : "-- FPS",
            OverlayWidgetType.CpuTempText => s.CpuTemp >= 0 ? $"{s.CpuTemp:F0}°C" : "--°C",
            OverlayWidgetType.CpuLoadText => s.CpuLoad >= 0 ? $"{s.CpuLoad:F0}%" : "--%",
            OverlayWidgetType.CpuClockText => s.CpuClock > 0 ? $"{s.CpuClock / 1000f:F1} GHz" : "-- GHz",
            OverlayWidgetType.CpuPowerText => s.CpuPower > 0 ? $"{s.CpuPower:F1} W" : "-- W",
            OverlayWidgetType.GpuTempText => s.GpuTemp >= 0 ? $"{s.GpuTemp:F0}°C" : "--°C",
            OverlayWidgetType.GpuLoadText => s.GpuLoad >= 0 ? $"{s.GpuLoad:F0}%" : "--%",
            OverlayWidgetType.GpuClockText => s.GpuClock > 0 ? $"{s.GpuClock:F0} MHz" : "-- MHz",
            OverlayWidgetType.GpuPowerText => s.GpuPower > 0 ? $"{s.GpuPower:F1} W" : "-- W",
            OverlayWidgetType.GpuVramText => s.GpuVramUsedGB >= 0 ? $"{s.GpuVramUsedGB:F1} GB" : "-- GB",
            OverlayWidgetType.MemLoadText => s.MemLoad >= 0 ? $"{s.MemLoad:F0}%" : "--%",
            OverlayWidgetType.MemUsedText => s.MemUsedGB >= 0 ? $"{s.MemUsedGB:F1} GB" : "-- GB",
            OverlayWidgetType.DiskReadText => s.DiskReadMBs >= 0 ? $"{s.DiskReadMBs:F1} MB/s" : "-- MB/s",
            OverlayWidgetType.DiskWriteText => s.DiskWriteMBs >= 0 ? $"{s.DiskWriteMBs:F1} MB/s" : "-- MB/s",
            OverlayWidgetType.NetUpText => s.NetUpMBs >= 0 ? $"{s.NetUpMBs:F2} MB/s" : "-- MB/s",
            OverlayWidgetType.NetDownText => s.NetDownMBs >= 0 ? $"{s.NetDownMBs:F2} MB/s" : "-- MB/s",
            OverlayWidgetType.CpuNameText => string.IsNullOrEmpty(s.CpuName) ? "CPU" : s.CpuName,
            OverlayWidgetType.GpuNameText => string.IsNullOrEmpty(s.GpuName) ? "GPU" : s.GpuName,
            _ => "--"
        };
    }

    private static (string? key, float value) GetChartValue(OverlayWidgetType type, MonitorSample s)
    {
        return type switch
        {
            OverlayWidgetType.FpsChart => ("fps", s.Fps >= 0 ? s.Fps : 0),
            OverlayWidgetType.CpuTempChart => ("cputemp", s.CpuTemp >= 0 ? s.CpuTemp : 0),
            _ => (null, 0)
        };
    }

    #endregion

    #region Public API

    public void SetTargetWindow(IntPtr hwnd) => _targetHwnd = hwnd;
    public void SetDesktopMode(bool desktopMode) => _desktopMode = desktopMode;
    public void SetPosition(OverlayPosition position) => _position = position;
    public void SetBackgroundOpacity(float opacity) => _bgOpacity = Math.Clamp(opacity, 0f, 1f);

    #endregion

    #region Dispose

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _topmostTimer?.Dispose();
        _topmostTimer = null;

        if (_hwnd != IntPtr.Zero && IsWindow(_hwnd))
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        // Dispose cached image bitmaps and per-widget render resources
        foreach (var w in _widgets)
        {
            w.CachedImage?.Dispose();
            w.CachedImage = null;
            ReleaseWidgetSurface(w);
            ReleaseWidgetSkia(w);
        }
        ReleaseSurface();

        if (_instance == this) _instance = null;
    }

    #endregion
}
