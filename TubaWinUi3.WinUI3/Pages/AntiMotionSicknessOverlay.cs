using System.Runtime.InteropServices;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed class AntiMotionSicknessOverlay
{
    private static AntiMotionSicknessOverlay? _instance;

    public static bool IsRunning => _instance is not null;

    public static bool ForceTopmostMode { get; set; } = false;

    public static void ShowOverlay()
    {
        if (_instance is not null) return;
        _instance = new AntiMotionSicknessOverlay();
        _instance.Create();
    }

    public static void CloseOverlay()
    {
        if (_instance is null) return;
        _instance.Destroy();
        _instance = null;
    }

    public static void RefreshVisuals()
    {
        _instance?.Render();
    }

    #region Win32 P/Invoke
    [DllImport("user32.dll")]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreatePen(int iStyle, int cWidth, uint color);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int i);

    [DllImport("gdi32.dll")]
    private static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern bool Ellipse(IntPtr hdc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern bool MoveToEx(IntPtr hdc, int X, int Y, out POINT lpPoint);

    [DllImport("gdi32.dll")]
    private static extern bool LineTo(IntPtr hdc, int nXEnd, int nYEnd);

    [DllImport("gdi32.dll")]
    private static extern bool Polygon(IntPtr hdc, POINT[] lpPoints, int nCount);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSW
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
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int PS_SOLID = 0;
    private const int SW_SHOW = 5;
    private const int NULL_BRUSH = 5;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_TIMER = 0x0113;
    private const int HTTRANSPARENT = -1;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const uint ULW_ALPHA = 0x02;
    private const uint DIB_RGB_COLORS = 0;
    private const uint BI_RGB = 0;

    private const int DWMWA_EXCLUDED_FROM_LIVEPREVIEW = 33;
    private const int DWMWA_CLOAK = 14;
    private const uint TIMER_ID_TOPMOST = 1;
    private const uint TIMER_INTERVAL_MS = 200;

    #endregion

    private IntPtr _hwnd;
    private WndProcDelegate? _wndProc;
    private static ushort _classAtom;
    private const string ClassName = "TubaAntiMSOvl3";
    private int _monitorX;
    private int _monitorY;
    private int _monitorW;
    private int _monitorH;

    private void Create()
    {
        RegisterClass();

        GetTargetMonitorBounds();

        var exStyle = WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;

        _hwnd = CreateWindowExW(
            (uint)exStyle,
            ClassName,
            "",
            WS_POPUP | WS_VISIBLE,
            _monitorX, _monitorY, _monitorW, _monitorH,
            IntPtr.Zero, IntPtr.Zero,
            Marshal.GetHINSTANCE(typeof(AntiMotionSicknessOverlay).Module),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero) return;

        var excluded = 1;
        DwmSetWindowAttribute(_hwnd, DWMWA_EXCLUDED_FROM_LIVEPREVIEW, ref excluded, sizeof(int));

        ShowWindow(_hwnd, SW_SHOW);
        SetWindowPos(_hwnd, HWND_TOPMOST, _monitorX, _monitorY, _monitorW, _monitorH, SWP_NOACTIVATE);

        if (ForceTopmostMode)
        {
            SetTimer(_hwnd, TIMER_ID_TOPMOST, TIMER_INTERVAL_MS, IntPtr.Zero);
        }

        Render();
    }

    private void GetTargetMonitorBounds()
    {
        _monitorX = 0;
        _monitorY = 0;
        _monitorW = GetSystemMetrics(SM_CXSCREEN);
        _monitorH = GetSystemMetrics(SM_CYSCREEN);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetTimer(IntPtr hWnd, uint nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr hWnd, uint uIDEvent);

    private void RegisterClass()
    {
        if (_classAtom != 0) return;

        _wndProc = WndProc;
        var wc = new WNDCLASSW
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = Marshal.GetHINSTANCE(typeof(AntiMotionSicknessOverlay).Module),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = GetStockObject(NULL_BRUSH),
            lpszMenuName = null!,
            lpszClassName = ClassName
        };
        _classAtom = RegisterClassW(ref wc);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                return (IntPtr)HTTRANSPARENT;

            case WM_TIMER:
                if (wParam == TIMER_ID_TOPMOST)
                {
                    SetWindowPos(hWnd, HWND_TOPMOST, _monitorX, _monitorY, _monitorW, _monitorH, SWP_NOACTIVATE);
                    return IntPtr.Zero;
                }
                break;

            case WM_DISPLAYCHANGE:
                GetTargetMonitorBounds();
                SetWindowPos(hWnd, HWND_TOPMOST, _monitorX, _monitorY, _monitorW, _monitorH, SWP_NOACTIVATE);
                Render();
                return IntPtr.Zero;

            case WM_DESTROY:
                KillTimer(hWnd, TIMER_ID_TOPMOST);
                _instance = null;
                return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void Render()
    {
        if (_hwnd == IntPtr.Zero) return;

        var screenW = _monitorW;
        var screenH = _monitorH;
        if (screenW <= 0 || screenH <= 0) return;

        var screenDC = GetDC(IntPtr.Zero);
        var memDC = CreateCompatibleDC(screenDC);

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = screenW,
                biHeight = screenH,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
                biSizeImage = 0,
                biXPelsPerMeter = 0,
                biYPelsPerMeter = 0,
                biClrUsed = 0,
                biClrImportant = 0
            }
        };

        var hBmp = CreateDIBSection(memDC, ref bmi, DIB_RGB_COLORS, out var pBits, IntPtr.Zero, 0);
        if (hBmp == IntPtr.Zero || pBits == IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, screenDC);
            DeleteDC(memDC);
            return;
        }

        var oldBmp = SelectObject(memDC, hBmp);

        var stride = screenW * 4;
        var totalBytes = stride * screenH;

        unsafe
        {
            var ptr = (byte*)pBits;
            for (var i = 0; i < totalBytes; i += 4)
            {
                ptr[i] = 0;
                ptr[i + 1] = 0;
                ptr[i + 2] = 0;
                ptr[i + 3] = 0;
            }
        }

        try
        {
            var cfg = AntiMotionSicknessConfig.Load();
            var cx = screenW / 2;
            var cy = screenH / 2;

            if (cfg.ShowCenter)
                DrawCrosshairGdi(memDC, cx, cy, cfg, cfg.CenterColor, cfg.Opacity);

            if (cfg.ShowTop)
                DrawEdgeMarkerGdi(memDC, cx, 0, cfg, cfg.EdgeColor, cfg.Opacity, 0);
            if (cfg.ShowBottom)
                DrawEdgeMarkerGdi(memDC, cx, screenH, cfg, cfg.EdgeColor, cfg.Opacity, 1);
            if (cfg.ShowLeft)
                DrawEdgeMarkerGdi(memDC, 0, cy, cfg, cfg.EdgeColor, cfg.Opacity, 2);
            if (cfg.ShowRight)
                DrawEdgeMarkerGdi(memDC, screenW, cy, cfg, cfg.EdgeColor, cfg.Opacity, 3);

            unsafe
            {
                var ptr = (byte*)pBits;
                var opacityByte = (byte)(cfg.Opacity * 255 / 100.0);
                for (var y = 0; y < screenH; y++)
                {
                    for (var x = 0; x < screenW; x++)
                    {
                        var off = (y * screenW + x) * 4;
                        var b = ptr[off];
                        var g = ptr[off + 1];
                        var r = ptr[off + 2];

                        if (r != 0 || g != 0 || b != 0)
                        {
                            ptr[off + 3] = opacityByte;
                        }
                    }
                }
            }

            var ptDst = new POINT { X = _monitorX, Y = _monitorY };
            var size = new SIZE { cx = screenW, cy = screenH };
            var ptSrc = new POINT { X = 0, Y = 0 };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };

            UpdateLayeredWindow(_hwnd, screenDC, ref ptDst, ref size, memDC, ref ptSrc, 0, ref blend, ULW_ALPHA);
        }
        catch { }
        finally
        {
            SelectObject(memDC, oldBmp);
            DeleteObject(hBmp);
            DeleteDC(memDC);
            ReleaseDC(IntPtr.Zero, screenDC);
        }
    }

    private static uint ColorToCOLORREF(Color color)
    {
        return (uint)(color.R | (color.G << 8) | (color.B << 16));
    }

    private static void DrawCrosshairGdi(IntPtr hdc, int cx, int cy, AntiMotionSicknessConfig cfg, Color color, double opacity)
    {
        var size = (int)cfg.CenterSize;
        var thickness = (int)cfg.CenterThickness;
        var colorRef = (uint)(color.R | (color.G << 8) | (color.B << 16));

        switch (cfg.CenterStyle)
        {
            case CrosshairStyle.Cross:
                {
                    var pen = CreatePen(PS_SOLID, thickness, colorRef);
                    var oldPen = SelectObject(hdc, pen);
                    MoveToEx(hdc, cx - size, cy, out _);
                    LineTo(hdc, cx + size, cy);
                    MoveToEx(hdc, cx, cy - size, out _);
                    LineTo(hdc, cx, cy + size);
                    SelectObject(hdc, oldPen);
                    DeleteObject(pen);
                }
                break;

            case CrosshairStyle.Dot:
                {
                    var brush = CreateSolidBrush(colorRef);
                    var oldBrush = SelectObject(hdc, brush);
                    var pen = CreatePen(PS_SOLID, 1, colorRef);
                    var oldPen = SelectObject(hdc, pen);
                    Ellipse(hdc, cx - size, cy - size, cx + size, cy + size);
                    SelectObject(hdc, oldPen);
                    SelectObject(hdc, oldBrush);
                    DeleteObject(pen);
                    DeleteObject(brush);
                }
                break;

            case CrosshairStyle.CrossDot:
                {
                    var pen = CreatePen(PS_SOLID, thickness, colorRef);
                    var oldPen = SelectObject(hdc, pen);
                    MoveToEx(hdc, cx - size, cy, out _);
                    LineTo(hdc, cx + size, cy);
                    MoveToEx(hdc, cx, cy - size, out _);
                    LineTo(hdc, cx, cy + size);
                    SelectObject(hdc, oldPen);
                    DeleteObject(pen);

                    var dotR = (int)(thickness * 1.5);
                    var brush = CreateSolidBrush(colorRef);
                    var oldBrush = SelectObject(hdc, brush);
                    pen = CreatePen(PS_SOLID, 1, colorRef);
                    oldPen = SelectObject(hdc, pen);
                    Ellipse(hdc, cx - dotR, cy - dotR, cx + dotR, cy + dotR);
                    SelectObject(hdc, oldPen);
                    SelectObject(hdc, oldBrush);
                    DeleteObject(pen);
                    DeleteObject(brush);
                }
                break;

            case CrosshairStyle.CrossCircle:
                {
                    var pen = CreatePen(PS_SOLID, thickness, colorRef);
                    var oldPen = SelectObject(hdc, pen);
                    MoveToEx(hdc, cx - size, cy, out _);
                    LineTo(hdc, cx + size, cy);
                    MoveToEx(hdc, cx, cy - size, out _);
                    LineTo(hdc, cx, cy + size);
                    SelectObject(hdc, oldPen);
                    DeleteObject(pen);

                    var circleR = (int)(size * 0.6);
                    var nullBrush = GetStockObject(NULL_BRUSH);
                    var oldHBrush = SelectObject(hdc, nullBrush);
                    var hollowPen = CreatePen(PS_SOLID, thickness, colorRef);
                    oldPen = SelectObject(hdc, hollowPen);
                    Ellipse(hdc, cx - circleR, cy - circleR, cx + circleR, cy + circleR);
                    SelectObject(hdc, oldPen);
                    SelectObject(hdc, oldHBrush);
                    DeleteObject(hollowPen);
                }
                break;

            case CrosshairStyle.TShape:
                {
                    var pen = CreatePen(PS_SOLID, thickness, colorRef);
                    var oldPen = SelectObject(hdc, pen);
                    MoveToEx(hdc, cx - size, cy, out _);
                    LineTo(hdc, cx + size, cy);
                    MoveToEx(hdc, cx, cy, out _);
                    LineTo(hdc, cx, cy + size);
                    SelectObject(hdc, oldPen);
                    DeleteObject(pen);
                }
                break;

            case CrosshairStyle.XShape:
                {
                    var pen = CreatePen(PS_SOLID, thickness, colorRef);
                    var oldPen = SelectObject(hdc, pen);
                    var offset = (int)(size * 0.707);
                    MoveToEx(hdc, cx - offset, cy - offset, out _);
                    LineTo(hdc, cx + offset, cy + offset);
                    MoveToEx(hdc, cx + offset, cy - offset, out _);
                    LineTo(hdc, cx - offset, cy + offset);
                    SelectObject(hdc, oldPen);
                    DeleteObject(pen);
                }
                break;

            case CrosshairStyle.Circle:
                {
                    var nullBrush = GetStockObject(NULL_BRUSH);
                    var oldBrush = SelectObject(hdc, nullBrush);
                    var pen = CreatePen(PS_SOLID, thickness, colorRef);
                    var oldPen = SelectObject(hdc, pen);
                    Ellipse(hdc, cx - size, cy - size, cx + size, cy + size);
                    SelectObject(hdc, oldPen);
                    SelectObject(hdc, oldBrush);
                    DeleteObject(pen);
                }
                break;

            case CrosshairStyle.CrossDotLarge:
                {
                    var pen = CreatePen(PS_SOLID, thickness, colorRef);
                    var oldPen = SelectObject(hdc, pen);
                    MoveToEx(hdc, cx - size, cy, out _);
                    LineTo(hdc, cx + size, cy);
                    MoveToEx(hdc, cx, cy - size, out _);
                    LineTo(hdc, cx, cy + size);
                    SelectObject(hdc, oldPen);
                    DeleteObject(pen);

                    var dotR = (int)(size * 0.4);
                    var brush = CreateSolidBrush(colorRef);
                    var oldBrush = SelectObject(hdc, brush);
                    pen = CreatePen(PS_SOLID, 1, colorRef);
                    oldPen = SelectObject(hdc, pen);
                    Ellipse(hdc, cx - dotR, cy - dotR, cx + dotR, cy + dotR);
                    SelectObject(hdc, oldPen);
                    SelectObject(hdc, oldBrush);
                    DeleteObject(pen);
                    DeleteObject(brush);
                }
                break;

            case CrosshairStyle.Split:
                {
                    var pen = CreatePen(PS_SOLID, thickness, colorRef);
                    var oldPen = SelectObject(hdc, pen);
                    var gap = (int)(thickness * 2);
                    MoveToEx(hdc, cx - size, cy, out _);
                    LineTo(hdc, cx - gap, cy);
                    MoveToEx(hdc, cx + gap, cy, out _);
                    LineTo(hdc, cx + size, cy);
                    MoveToEx(hdc, cx, cy - size, out _);
                    LineTo(hdc, cx, cy - gap);
                    MoveToEx(hdc, cx, cy + gap, out _);
                    LineTo(hdc, cx, cy + size);
                    SelectObject(hdc, oldPen);
                    DeleteObject(pen);
                }
                break;

            case CrosshairStyle.Crosshatch:
                {
                    var pen = CreatePen(PS_SOLID, thickness, colorRef);
                    var oldPen = SelectObject(hdc, pen);
                    MoveToEx(hdc, cx - size, cy, out _);
                    LineTo(hdc, cx + size, cy);
                    MoveToEx(hdc, cx, cy - size, out _);
                    LineTo(hdc, cx, cy + size);
                    var offset = (int)(size * 0.707);
                    MoveToEx(hdc, cx - offset, cy - offset, out _);
                    LineTo(hdc, cx + offset, cy + offset);
                    MoveToEx(hdc, cx + offset, cy - offset, out _);
                    LineTo(hdc, cx - offset, cy + offset);
                    SelectObject(hdc, oldPen);
                    DeleteObject(pen);
                }
                break;

            case CrosshairStyle.DoubleCircle:
                {
                    var nullBrush = GetStockObject(NULL_BRUSH);
                    var oldBrush = SelectObject(hdc, nullBrush);
                    var pen = CreatePen(PS_SOLID, thickness, colorRef);
                    var oldPen = SelectObject(hdc, pen);
                    Ellipse(hdc, cx - size, cy - size, cx + size, cy + size);
                    var innerR = (int)(size * 0.6);
                    Ellipse(hdc, cx - innerR, cy - innerR, cx + innerR, cy + innerR);
                    SelectObject(hdc, oldPen);
                    SelectObject(hdc, oldBrush);
                    DeleteObject(pen);
                }
                break;

            case CrosshairStyle.CrossGap:
                {
                    var pen = CreatePen(PS_SOLID, thickness, colorRef);
                    var oldPen = SelectObject(hdc, pen);
                    var gap = (int)(size * 0.3);
                    MoveToEx(hdc, cx - size, cy, out _);
                    LineTo(hdc, cx - gap, cy);
                    MoveToEx(hdc, cx + gap, cy, out _);
                    LineTo(hdc, cx + size, cy);
                    MoveToEx(hdc, cx, cy - size, out _);
                    LineTo(hdc, cx, cy - gap);
                    MoveToEx(hdc, cx, cy + gap, out _);
                    LineTo(hdc, cx, cy + size);
                    SelectObject(hdc, oldPen);
                    DeleteObject(pen);
                }
                break;
        }
    }

    private static void DrawEdgeMarkerGdi(IntPtr hdc, int x, int y, AntiMotionSicknessConfig cfg, Color color, double opacity, int posIndex)
    {
        var size = (int)cfg.EdgeSize;
        var colorRef = (uint)(color.R | (color.G << 8) | (color.B << 16));
        int drawX, drawY;

        switch (posIndex)
        {
            case 0: drawX = x - size / 2; drawY = 0; break;
            case 1: drawX = x - size / 2; drawY = y - size; break;
            case 2: drawX = 0; drawY = y - size / 2; break;
            case 3: drawX = x - size; drawY = y - size / 2; break;
            default: drawX = x; drawY = y; break;
        }

        var brush = CreateSolidBrush(colorRef);
        var oldBrush = SelectObject(hdc, brush);
        var pen = CreatePen(PS_SOLID, 1, colorRef);
        var oldPen = SelectObject(hdc, pen);

        switch (cfg.EdgeShape)
        {
            case EdgeMarkerShape.Square:
                Rectangle(hdc, drawX, drawY, drawX + size, drawY + size);
                break;

            case EdgeMarkerShape.Circle:
                Ellipse(hdc, drawX, drawY, drawX + size, drawY + size);
                break;

            case EdgeMarkerShape.Diamond:
                {
                    var half = size / 2;
                    var pts = new POINT[]
                    {
                        new() { X = drawX + half, Y = drawY },
                        new() { X = drawX + size, Y = drawY + half },
                        new() { X = drawX + half, Y = drawY + size },
                        new() { X = drawX, Y = drawY + half }
                    };
                    Polygon(hdc, pts, 4);
                }
                break;

            case EdgeMarkerShape.Triangle:
                {
                    var half = size / 2;
                    var pts = new POINT[]
                    {
                        new() { X = drawX + half, Y = drawY },
                        new() { X = drawX + size, Y = drawY + size },
                        new() { X = drawX, Y = drawY + size }
                    };
                    Polygon(hdc, pts, 3);
                }
                break;

            case EdgeMarkerShape.Plus:
                {
                    var half = size / 2;
                    var pen2 = CreatePen(PS_SOLID, Math.Max(1, size / 4), colorRef);
                    var oldPen2 = SelectObject(hdc, pen2);
                    MoveToEx(hdc, drawX + half, drawY, out _);
                    LineTo(hdc, drawX + half, drawY + size);
                    MoveToEx(hdc, drawX, drawY + half, out _);
                    LineTo(hdc, drawX + size, drawY + half);
                    SelectObject(hdc, oldPen2);
                    DeleteObject(pen2);
                    SelectObject(hdc, oldBrush);
                    DeleteObject(brush);
                    return;
                }

            case EdgeMarkerShape.Line:
                {
                    var pen2 = CreatePen(PS_SOLID, Math.Max(1, size / 4), colorRef);
                    var oldPen2 = SelectObject(hdc, pen2);
                    var half = size / 2;
                    if (posIndex == 0 || posIndex == 1)
                        MoveToEx(hdc, drawX, drawY + half, out _);
                    else
                        MoveToEx(hdc, drawX + half, drawY, out _);
                    if (posIndex == 0 || posIndex == 1)
                        LineTo(hdc, drawX + size, drawY + half);
                    else
                        LineTo(hdc, drawX + half, drawY + size);
                    SelectObject(hdc, oldPen2);
                    DeleteObject(pen2);
                    SelectObject(hdc, oldBrush);
                    DeleteObject(brush);
                    return;
                }

            case EdgeMarkerShape.Cross:
                {
                    var half = size / 2;
                    var pen2 = CreatePen(PS_SOLID, Math.Max(1, size / 4), colorRef);
                    var oldPen2 = SelectObject(hdc, pen2);
                    MoveToEx(hdc, drawX + half - size / 3, drawY + half - size / 3, out _);
                    LineTo(hdc, drawX + half + size / 3, drawY + half + size / 3);
                    MoveToEx(hdc, drawX + half + size / 3, drawY + half - size / 3, out _);
                    LineTo(hdc, drawX + half - size / 3, drawY + half + size / 3);
                    SelectObject(hdc, oldPen2);
                    DeleteObject(pen2);
                    SelectObject(hdc, oldBrush);
                    DeleteObject(brush);
                    return;
                }

            case EdgeMarkerShape.Star:
                {
                    var half = size / 2;
                    var innerR = size / 4;
                    var pts = new POINT[10];
                    for (var i = 0; i < 10; i++)
                    {
                        var angle = i * 36 - 90;
                        var rad = angle * Math.PI / 180.0;
                        var radius = i % 2 == 0 ? half : innerR;
                        pts[i] = new POINT
                        {
                            X = drawX + half + (int)(radius * Math.Cos(rad)),
                            Y = drawY + half + (int)(radius * Math.Sin(rad))
                        };
                    }
                    Polygon(hdc, pts, 10);
                }
                break;

            case EdgeMarkerShape.Ring:
                {
                    SelectObject(hdc, oldBrush);
                    DeleteObject(brush);
                    var nullBrush = GetStockObject(NULL_BRUSH);
                    oldBrush = SelectObject(hdc, nullBrush);
                    var ringPen = CreatePen(PS_SOLID, Math.Max(1, size / 5), colorRef);
                    var oldRingPen = SelectObject(hdc, ringPen);
                    Ellipse(hdc, drawX, drawY, drawX + size, drawY + size);
                    SelectObject(hdc, oldRingPen);
                    SelectObject(hdc, oldBrush);
                    DeleteObject(ringPen);
                    SelectObject(hdc, oldPen);
                    DeleteObject(pen);
                    return;
                }
        }

        SelectObject(hdc, oldPen);
        SelectObject(hdc, oldBrush);
        DeleteObject(pen);
        DeleteObject(brush);
    }

    private void Destroy()
    {
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}

public enum CrosshairStyle
{
    Cross,
    Dot,
    CrossDot,
    CrossCircle,
    TShape,
    XShape,
    Circle,
    CrossDotLarge,
    Split,
    Crosshatch,
    DoubleCircle,
    CrossGap
}

public enum EdgeMarkerShape
{
    Square,
    Circle,
    Diamond,
    Triangle,
    Plus,
    Line,
    Cross,
    Star,
    Ring
}

public sealed class AntiMotionSicknessConfig
{
    private const string Prefix = "AntiMotionSickness_";

    public Color CenterColor = Color.FromArgb(255, 0, 255, 0);
    public double CenterSize = 12;
    public double CenterThickness = 2;
    public CrosshairStyle CenterStyle = CrosshairStyle.Cross;

    public Color EdgeColor = Color.FromArgb(255, 0, 255, 0);
    public double EdgeSize = 10;
    public EdgeMarkerShape EdgeShape = EdgeMarkerShape.Square;

    public double Opacity = 80;

    public bool ShowCenter = true;
    public bool ShowTop = true;
    public bool ShowBottom = true;
    public bool ShowLeft = true;
    public bool ShowRight = true;

    public static AntiMotionSicknessConfig Load()
    {
        var cfg = new AntiMotionSicknessConfig();
        try
        {
            cfg.CenterColor = LoadColor(Prefix + "CenterColor", cfg.CenterColor);
            cfg.CenterSize = AppSettings.GetDouble(Prefix + "CenterSize", cfg.CenterSize);
            cfg.CenterThickness = AppSettings.GetDouble(Prefix + "CenterThickness", cfg.CenterThickness);
            cfg.CenterStyle = Enum.TryParse<CrosshairStyle>(AppSettings.Get(Prefix + "CenterStyle"), out var cs) ? cs : cfg.CenterStyle;

            cfg.EdgeColor = LoadColor(Prefix + "EdgeColor", cfg.EdgeColor);
            cfg.EdgeSize = AppSettings.GetDouble(Prefix + "EdgeSize", cfg.EdgeSize);
            cfg.EdgeShape = Enum.TryParse<EdgeMarkerShape>(AppSettings.Get(Prefix + "EdgeShape"), out var es) ? es : cfg.EdgeShape;

            cfg.Opacity = AppSettings.GetDouble(Prefix + "Opacity", cfg.Opacity);

            cfg.ShowCenter = AppSettings.GetBool(Prefix + "ShowCenter", cfg.ShowCenter);
            cfg.ShowTop = AppSettings.GetBool(Prefix + "ShowTop", cfg.ShowTop);
            cfg.ShowBottom = AppSettings.GetBool(Prefix + "ShowBottom", cfg.ShowBottom);
            cfg.ShowLeft = AppSettings.GetBool(Prefix + "ShowLeft", cfg.ShowLeft);
            cfg.ShowRight = AppSettings.GetBool(Prefix + "ShowRight", cfg.ShowRight);
        }
        catch { }

        return cfg;
    }

    public void Save()
    {
        try
        {
            SaveColor(Prefix + "CenterColor", CenterColor);
            AppSettings.Set(Prefix + "CenterSize", CenterSize);
            AppSettings.Set(Prefix + "CenterThickness", CenterThickness);
            AppSettings.Set(Prefix + "CenterStyle", CenterStyle.ToString());

            SaveColor(Prefix + "EdgeColor", EdgeColor);
            AppSettings.Set(Prefix + "EdgeSize", EdgeSize);
            AppSettings.Set(Prefix + "EdgeShape", EdgeShape.ToString());

            AppSettings.Set(Prefix + "Opacity", Opacity);

            AppSettings.Set(Prefix + "ShowCenter", ShowCenter);
            AppSettings.Set(Prefix + "ShowTop", ShowTop);
            AppSettings.Set(Prefix + "ShowBottom", ShowBottom);
            AppSettings.Set(Prefix + "ShowLeft", ShowLeft);
            AppSettings.Set(Prefix + "ShowRight", ShowRight);
        }
        catch { }
    }

    private static Color LoadColor(string key, Color defaultColor)
    {
        var s = AppSettings.Get(key);
        if (s is not null)
        {
            var parts = s.Split(',');
            if (parts.Length == 4 &&
                byte.TryParse(parts[0], out var a) &&
                byte.TryParse(parts[1], out var r) &&
                byte.TryParse(parts[2], out var g) &&
                byte.TryParse(parts[3], out var b))
                return Color.FromArgb(a, r, g, b);
        }
        return defaultColor;
    }

    private static void SaveColor(string key, Color color)
    {
        AppSettings.Set(key, $"{color.A},{color.R},{color.G},{color.B}");
    }
}
