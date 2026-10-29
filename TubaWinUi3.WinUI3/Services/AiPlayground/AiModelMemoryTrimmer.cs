using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>
/// 本地大模型卸载后的内存回收：ORT/GenAI 释放的大块 arena 内存默认留在进程工作集里
/// （表现为"关掉页面内存不降"）。卸载后强制一次 GC 并裁剪工作集，把内存还给系统。
/// 只在模型卸载时调用（一次性开销可控），不要在频繁路径使用。
/// </summary>
internal static class AiModelMemoryTrimmer
{
    public static void Trim()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        }
        catch
        {
        }

        try
        {
            EmptyWorkingSet(GetCurrentProcess());
        }
        catch
        {
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);
}
