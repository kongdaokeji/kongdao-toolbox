using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace TubaWinUi3.Services;

/// <summary>
/// 重启 Windows 资源管理器（外壳进程 explorer.exe），用于让右键菜单改动生效。
/// 本应用始终以管理员身份运行：若直接 Process.Start("explorer.exe")，新的外壳会继承
/// 提权令牌（之后拖放、部分外壳扩展与打包应用交互都会异常），因此这里在结束旧外壳之前
/// 先复制它的用户令牌，再用该令牌把 explorer.exe 以当前用户身份重新拉起来。
/// 令牌复制或受限启动失败时退回普通启动，保证桌面至少能恢复。
/// </summary>
public static class ExplorerShellService
{
    /// <summary>结束全部 explorer.exe 进程并重新启动一个新外壳。</summary>
    public static void Restart()
    {
        IntPtr shellToken = TryCaptureShellToken();
        try
        {
            Process[] shells = Process.GetProcessesByName("explorer");
            int failed = 0;
            foreach (Process shell in shells)
            {
                using (shell)
                {
                    try
                    {
                        shell.Kill();
                        if (!shell.WaitForExit(10000)) failed++;
                    }
                    catch
                    {
                        failed++;
                    }
                }
            }
            // 一个都结束不了说明权限不足（打包/非提权运行），此时不要拉起第二个外壳
            if (shells.Length > 0 && failed == shells.Length)
                throw new InvalidOperationException("无法结束资源管理器进程，请以管理员身份运行本工具后重试。");
            StartExplorer(shellToken);
        }
        finally
        {
            if (shellToken != IntPtr.Zero) CloseHandle(shellToken);
        }
    }

    /// <summary>结束旧外壳之前取一次它的用户令牌（提权进程拿不到非提权令牌，只能用这一份）。</summary>
    private static IntPtr TryCaptureShellToken()
    {
        foreach (Process shell in Process.GetProcessesByName("explorer"))
        {
            using (shell)
            {
                IntPtr processHandle = OpenProcess(ProcessQueryLimitedInformation, false, shell.Id);
                if (processHandle == IntPtr.Zero) continue;
                try
                {
                    if (!OpenProcessToken(processHandle, TokenQuery | TokenDuplicate, out IntPtr token)) continue;
                    try
                    {
                        if (DuplicateTokenEx(token, TokenAllAccess, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out IntPtr primary)) return primary;
                    }
                    finally
                    {
                        CloseHandle(token);
                    }
                }
                finally
                {
                    CloseHandle(processHandle);
                }
            }
        }
        return IntPtr.Zero;
    }

    private static void StartExplorer(IntPtr shellToken)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (shellToken != IntPtr.Zero && StartWithToken(shellToken, path) && WaitForShell(8000)) return;
        // 受限启动失败、或新外壳没能存活：退回普通启动，保证任务栏与桌面能恢复
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }

    private static bool StartWithToken(IntPtr shellToken, string path)
    {
        var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        if (!CreateProcessWithTokenW(shellToken, 0, path, "\"" + path + "\"", 0, IntPtr.Zero, null, ref startup, out ProcessInformation information)) return false;
        CloseHandle(information.hProcess);
        CloseHandle(information.hThread);
        return true;
    }

    private static bool WaitForShell(int milliseconds)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (true)
        {
            Process[] shells = Process.GetProcessesByName("explorer");
            foreach (Process shell in shells) shell.Dispose();
            if (shells.Length > 0) return true;
            if (DateTime.UtcNow >= deadline) return false;
            Thread.Sleep(200);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenAllAccess = 0x000F01FF;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr existingToken, uint desiredAccess, IntPtr tokenAttributes, int impersonationLevel, int tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string applicationName, string commandLine, uint creationFlags, IntPtr environment, string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
