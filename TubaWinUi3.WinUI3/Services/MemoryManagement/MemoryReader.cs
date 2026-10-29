using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TubaWinUi3.Services.MemoryManagement;

public static class MemoryReader
{
    public static MemorySnapshot Read()
    {
        var s = new MemoryNative.Status { Length = (uint)Marshal.SizeOf<MemoryNative.Status>() };
        MemoryNative.Check(MemoryNative.GlobalMemoryStatusEx(ref s));
        var p = new MemoryNative.Performance { Size = (uint)Marshal.SizeOf<MemoryNative.Performance>() };
        MemoryNative.Check(MemoryNative.GetPerformanceInfo(ref p, p.Size));
        MemoryNative.GetPhysicallyInstalledSystemMemory(out var installed);
        ulong? free = null, zero = null, modified = null;
        ulong[]? standby = null;
        string? error = null;
        // SYSTEM_MEMORY_LIST_INFORMATION: 22 pointer-sized page counts (PHNT).
        int length = 22 * IntPtr.Size;
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            int status = MemoryNative.NtQuerySystemInformation(80, buffer, (uint)length, out uint returned);
            if (status < 0 || returned < length) error = $"Memory lists unavailable (NTSTATUS 0x{status:X8}, length {returned})";
            else
            {
                ulong Count(int i) => (IntPtr.Size == 8 ? unchecked((ulong)Marshal.ReadInt64(buffer, i * 8)) : unchecked((uint)Marshal.ReadInt32(buffer, i * 4))) * (ulong)p.PageSize;
                zero = Count(0); free = Count(1); modified = Count(2);
                standby = Enumerable.Range(5, 8).Select(Count).ToArray();
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return new(DateTimeOffset.Now, installed * 1024, s.Total, s.Available,
            (ulong)p.Commit * (ulong)p.PageSize, (ulong)p.Limit * (ulong)p.PageSize,
            (ulong)p.Peak * (ulong)p.PageSize, (ulong)p.Cache * (ulong)p.PageSize,
            (ulong)p.Paged * (ulong)p.PageSize, (ulong)p.Nonpaged * (ulong)p.PageSize,
            free, zero, modified, standby, error);
    }

}
