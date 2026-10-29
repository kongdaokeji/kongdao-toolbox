using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TubaWinUi3.Services.MemoryManagement;

internal static class MemoryNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Status { public uint Length, Load; public ulong Total, Available, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, Extended; }
    [StructLayout(LayoutKind.Sequential)] internal struct Performance { public uint Size; public nuint Commit, Limit, Peak, Total, Available, Cache, KernelTotal, Paged, Nonpaged, PageSize; public uint Handles, Processes, Threads; }
    [StructLayout(LayoutKind.Sequential)] internal struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] internal struct Privileges { public uint Count; public Luid Id; public uint Attributes; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GlobalMemoryStatusEx(ref Status status);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetPhysicallyInstalledSystemMemory(out ulong kib);
    [DllImport("psapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetPerformanceInfo(ref Performance info, uint size);
    [DllImport("ntdll.dll")] internal static extern int NtQuerySystemInformation(int type, IntPtr data, uint length, out uint returned);
    [DllImport("ntdll.dll")] internal static extern int NtSetSystemInformation(int type, ref int command, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsProcessCritical(SafeProcessHandle process, out bool critical);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetProcessInformation(SafeProcessHandle process, int infoClass, out uint protection, uint size);
    [DllImport("psapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EmptyWorkingSet(SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetSystemFileCacheSize(nuint minimum, nuint maximum, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out int pid);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disable, ref Privileges state, uint length, out Privileges previous, out uint returned);
    internal static void Check(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }

    // Enable only for the operation; restore the previous token state on every exit path.
    internal sealed class PrivilegeScope : IDisposable
    {
        private static readonly object Gate = new();
        private readonly SafeAccessTokenHandle _token;
        private Privileges _previous;
        internal PrivilegeScope(string name)
        {
            Monitor.Enter(Gate);
            try
            {
                Check(OpenProcessToken(new IntPtr(-1), 0x28, out _token));
                Check(LookupPrivilegeValue(null, name, out var id));
                var state = new Privileges { Count = 1, Id = id, Attributes = 2 };
                Check(AdjustTokenPrivileges(_token, false, ref state, (uint)Marshal.SizeOf<Privileges>(), out _previous, out _));
                if (Marshal.GetLastWin32Error() == 1300) throw new Win32Exception(1300);
            }
            catch { _token?.Dispose(); Monitor.Exit(Gate); throw; }
        }
        public void Dispose() { try { AdjustTokenPrivileges(_token, false, ref _previous, 0, out _, out _); _token.Dispose(); } finally { Monitor.Exit(Gate); } }
    }
}
