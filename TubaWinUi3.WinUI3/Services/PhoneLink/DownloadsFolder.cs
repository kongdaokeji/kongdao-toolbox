using System.Runtime.InteropServices;

namespace TubaWinUi3.Services;

/// <summary>
/// 定位 Windows「下载」文件夹（手机发来的文件直接落这里，不再另存数据目录副本）。
/// 优先通过 shell 已知文件夹解析（兼容用户改过下载目录的情况），失败回退 %USERPROFILE%\Downloads。
/// </summary>
internal static class DownloadsFolder
{
    private static readonly Guid FolderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    public static string Resolve()
    {
        var path = TryGetKnownFolder();
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
        try { Directory.CreateDirectory(path); } catch { }
        return path;
    }

    private static string? TryGetKnownFolder()
    {
        var ptr = IntPtr.Zero;
        try
        {
            var id = FolderIdDownloads;
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out ptr) == 0 && ptr != IntPtr.Zero)
            {
                return Marshal.PtrToStringUni(ptr);
            }
        }
        catch
        {
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr);
        }
        return null;
    }
}
