using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services;

public enum PortTcpState
{
    Closed = 1,
    Listen = 2,
    SynSent = 3,
    SynReceived = 4,
    Established = 5,
    FinWait1 = 6,
    FinWait2 = 7,
    CloseWait = 8,
    Closing = 9,
    LastAck = 10,
    TimeWait = 11,
    DeleteTcb = 12,
    Unknown = 0
}

public sealed class PortEntry
{
    public string Protocol { get; init; } = "";            // "TCP" / "UDP" (base，过滤用)
    public IPAddress LocalAddress { get; init; } = IPAddress.None;
    public int LocalPort { get; init; }
    public IPAddress RemoteAddress { get; init; } = IPAddress.None;
    public int RemotePort { get; init; }
    public PortTcpState State { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";

    /// <summary>显示用的协议标签，IPv6 会带上 6 后缀（TCP6/UDP6）。</summary>
    public string ProtocolLabel =>
        LocalAddress.AddressFamily == AddressFamily.InterNetworkV6
            ? Protocol + "6"
            : Protocol;

    public bool IsIPv6 => LocalAddress.AddressFamily == AddressFamily.InterNetworkV6;
}

public static class PortViewerService
{
    public static async Task<List<PortEntry>> ScanAsync()
    {
        return await Task.Run(Scan);
    }

    public static bool KillProcess(int pid, out string error)
    {
        error = "";
        if (pid == 0 || pid == 4)
        {
            error = "无法结束 System 进程";
            return false;
        }
        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.Kill();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static List<PortEntry> Scan()
    {
        // 进程名缓存：同一 PID 的多个端口只查一次 Process.GetProcessById
        var procCache = new Dictionary<int, string>();

        string GetName(int pid)
        {
            if (pid == 0 || pid == 4) return "System";
            if (procCache.TryGetValue(pid, out var n)) return n;
            try
            {
                using var p = Process.GetProcessById(pid);
                return procCache[pid] = p.ProcessName;
            }
            catch
            {
                return procCache[pid] = $"PID:{pid}";
            }
        }

        var entries = new List<PortEntry>();

        // TCP IPv4
        foreach (var r in GetTcpTableV4())
        {
            entries.Add(new PortEntry
            {
                Protocol = "TCP",
                LocalAddress = r.LocalAddress,
                LocalPort = r.LocalPort,
                RemoteAddress = r.RemoteAddress,
                RemotePort = r.RemotePort,
                State = r.State,
                ProcessId = r.OwningPid,
                ProcessName = GetName(r.OwningPid)
            });
        }

        // TCP IPv6
        foreach (var r in GetTcpTableV6())
        {
            entries.Add(new PortEntry
            {
                Protocol = "TCP",
                LocalAddress = r.LocalAddress,
                LocalPort = r.LocalPort,
                RemoteAddress = r.RemoteAddress,
                RemotePort = r.RemotePort,
                State = r.State,
                ProcessId = r.OwningPid,
                ProcessName = GetName(r.OwningPid)
            });
        }

        // UDP IPv4
        foreach (var r in GetUdpTableV4())
        {
            entries.Add(new PortEntry
            {
                Protocol = "UDP",
                LocalAddress = r.LocalAddress,
                LocalPort = r.LocalPort,
                RemoteAddress = IPAddress.None,
                RemotePort = 0,
                State = PortTcpState.Unknown,
                ProcessId = r.OwningPid,
                ProcessName = GetName(r.OwningPid)
            });
        }

        // UDP IPv6
        foreach (var r in GetUdpTableV6())
        {
            entries.Add(new PortEntry
            {
                Protocol = "UDP",
                LocalAddress = r.LocalAddress,
                LocalPort = r.LocalPort,
                RemoteAddress = IPAddress.None,
                RemotePort = 0,
                State = PortTcpState.Unknown,
                ProcessId = r.OwningPid,
                ProcessName = GetName(r.OwningPid)
            });
        }

        return entries
            .OrderBy(e => e.Protocol)
            .ThenBy(e => e.IsIPv6 ? 1 : 0)   // IPv4 在前，IPv6 在后
            .ThenBy(e => e.LocalPort)
            .ToList();
    }

    // ---------- 表读取：统一使用 GetExtendedTcpTable / GetExtendedUdpTable ----------
    // 这两个 API 才会返回正确的 OwningPid（进程名不丢失），并支持 IPv6。

    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int UDP_TABLE_OWNER_PID = 1;

    private static List<TcpV4Row> GetTcpTableV4()
    {
        var result = new List<TcpV4Row>();
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0) return result;

        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(ptr, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return result;

            var count = Marshal.ReadInt32(ptr);
            var rowPtr = ptr + 4;
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                result.Add(new TcpV4Row(row));
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return result;
    }

    private static List<TcpV6Row> GetTcpTableV6()
    {
        var result = new List<TcpV6Row>();
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0) return result;

        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(ptr, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return result;

            var count = Marshal.ReadInt32(ptr);
            var rowPtr = ptr + 4;
            var rowSize = Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(rowPtr);
                result.Add(new TcpV6Row(row));
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return result;
    }

    private static List<UdpV4Row> GetUdpTableV4()
    {
        var result = new List<UdpV4Row>();
        var size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
        if (size == 0) return result;

        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedUdpTable(ptr, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0) != 0)
                return result;

            var count = Marshal.ReadInt32(ptr);
            var rowPtr = ptr + 4;
            var rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                result.Add(new UdpV4Row(row));
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return result;
    }

    private static List<UdpV6Row> GetUdpTableV6()
    {
        var result = new List<UdpV6Row>();
        var size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, false, AF_INET6, UDP_TABLE_OWNER_PID, 0);
        if (size == 0) return result;

        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedUdpTable(ptr, ref size, false, AF_INET6, UDP_TABLE_OWNER_PID, 0) != 0)
                return result;

            var count = Marshal.ReadInt32(ptr);
            var rowPtr = ptr + 4;
            var rowSize = Marshal.SizeOf<MIB_UDP6ROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDP6ROW_OWNER_PID>(rowPtr);
                result.Add(new UdpV6Row(row));
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return result;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder,
        int ulAf, int tableClass, int reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedUdpTable(IntPtr pUdpTable, ref int pdwSize, bool bOrder,
        int ulAf, int tableClass, int reserved);

    [DllImport("ws2_32.dll")]
    private static extern ushort ntohs(uint netshort);

    // ---------- 结构体定义 ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ucLocalAddr;
        public uint dwLocalScopeId;
        public uint dwLocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ucRemoteAddr;
        public uint dwRemoteScopeId;
        public uint dwRemotePort;
        public uint dwState;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ucLocalAddr;
        public uint dwLocalScopeId;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    private sealed class TcpV4Row
    {
        public IPAddress LocalAddress;
        public int LocalPort;
        public IPAddress RemoteAddress;
        public int RemotePort;
        public PortTcpState State;
        public int OwningPid;

        public TcpV4Row(MIB_TCPROW_OWNER_PID row)
        {
            LocalAddress = new IPAddress(row.dwLocalAddr);
            LocalPort = ntohs(row.dwLocalPort);
            RemoteAddress = new IPAddress(row.dwRemoteAddr);
            RemotePort = ntohs(row.dwRemotePort);
            State = (PortTcpState)row.dwState;
            OwningPid = (int)row.dwOwningPid;
        }
    }

    private sealed class TcpV6Row
    {
        public IPAddress LocalAddress;
        public int LocalPort;
        public IPAddress RemoteAddress;
        public int RemotePort;
        public PortTcpState State;
        public int OwningPid;

        public TcpV6Row(MIB_TCP6ROW_OWNER_PID row)
        {
            LocalAddress = new IPAddress(row.ucLocalAddr, row.dwLocalScopeId);
            LocalPort = ntohs(row.dwLocalPort);
            RemoteAddress = new IPAddress(row.ucRemoteAddr, row.dwRemoteScopeId);
            RemotePort = ntohs(row.dwRemotePort);
            State = (PortTcpState)row.dwState;
            OwningPid = (int)row.dwOwningPid;
        }
    }

    private sealed class UdpV4Row
    {
        public IPAddress LocalAddress;
        public int LocalPort;
        public int OwningPid;

        public UdpV4Row(MIB_UDPROW_OWNER_PID row)
        {
            LocalAddress = new IPAddress(row.dwLocalAddr);
            LocalPort = ntohs(row.dwLocalPort);
            OwningPid = (int)row.dwOwningPid;
        }
    }

    private sealed class UdpV6Row
    {
        public IPAddress LocalAddress;
        public int LocalPort;
        public int OwningPid;

        public UdpV6Row(MIB_UDP6ROW_OWNER_PID row)
        {
            LocalAddress = new IPAddress(row.ucLocalAddr, row.dwLocalScopeId);
            LocalPort = ntohs(row.dwLocalPort);
            OwningPid = (int)row.dwOwningPid;
        }
    }
}
