using System.Runtime.InteropServices;
using System.Text;

namespace TubaWinUi3.PECompatible;

internal static class WindowsClipboard
{
    private const uint CfBitmap = 2;
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const uint Srccopy = 0x00CC0020;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr objectHandle);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr destination,
        int x,
        int y,
        int width,
        int height,
        IntPtr source,
        int sourceX,
        int sourceY,
        uint operation);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr objectHandle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static bool SetText(string text)
    {
        var bytes = Encoding.Unicode.GetBytes((text ?? string.Empty) + '\0');
        var globalMemory = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
        if (globalMemory == IntPtr.Zero)
        {
            return false;
        }

        var lockedMemory = GlobalLock(globalMemory);
        if (lockedMemory == IntPtr.Zero)
        {
            GlobalFree(globalMemory);
            return false;
        }

        Marshal.Copy(bytes, 0, lockedMemory, bytes.Length);
        GlobalUnlock(globalMemory);

        if (!OpenClipboard(IntPtr.Zero))
        {
            GlobalFree(globalMemory);
            return false;
        }

        var transferred = false;
        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            transferred = SetClipboardData(CfUnicodeText, globalMemory) != IntPtr.Zero;
            return transferred;
        }
        finally
        {
            CloseClipboard();
            if (!transferred)
            {
                GlobalFree(globalMemory);
            }
        }
    }

    public static bool CopyWindowToClipboard()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || !GetWindowRect(window, out var rect))
        {
            return false;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var sourceDc = GetWindowDC(window);
        if (sourceDc == IntPtr.Zero)
        {
            return false;
        }

        var memoryDc = CreateCompatibleDC(sourceDc);
        var bitmap = memoryDc == IntPtr.Zero
            ? IntPtr.Zero
            : CreateCompatibleBitmap(sourceDc, width, height);
        if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            ReleaseDC(window, sourceDc);
            return false;
        }

        var oldBitmap = SelectObject(memoryDc, bitmap);
        var captured = BitBlt(memoryDc, 0, 0, width, height, sourceDc, 0, 0, Srccopy);
        if (oldBitmap != IntPtr.Zero)
        {
            SelectObject(memoryDc, oldBitmap);
        }
        DeleteDC(memoryDc);
        ReleaseDC(window, sourceDc);

        if (!captured || !OpenClipboard(IntPtr.Zero))
        {
            DeleteObject(bitmap);
            return false;
        }

        var transferred = false;
        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            transferred = SetClipboardData(CfBitmap, bitmap) != IntPtr.Zero;
            return transferred;
        }
        finally
        {
            CloseClipboard();
            if (!transferred)
            {
                DeleteObject(bitmap);
            }
        }
    }
}
