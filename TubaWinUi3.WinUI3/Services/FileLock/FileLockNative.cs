using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.FileLock;

/// <summary>
/// 「文件占用查看」的原生互操作声明（ntdll / kernel32）。
/// 移植自 PowerToys File Locksmith（MIT License，Copyright (c) Microsoft Corporation）的
/// 句柄枚举思路，此处为纯托管 + P/Invoke 的 WinUI 3 实现，零第三方依赖。
///
/// 注意：ULONG_PTR 宽度的字段一律用 IntPtr/UIntPtr 承载。若写成 uint，x64 下会把
/// 句柄值/进程 ID 截断成低 32 位，导致误判（例如 PID 会被截成随机值）。
/// </summary>
internal static class FileLockNative
{
    // ---------- ntdll ----------

    /// <summary>SYSTEM_INFORMATION_CLASS.SystemExtendedHandleInformation。</summary>
    internal const int SystemExtendedHandleInformation = 64;

    /// <summary>OBJECT_INFORMATION_CLASS.ObjectNameInformation。</summary>
    internal const int ObjectNameInformation = 1;

    /// <summary>STATUS_INFO_LENGTH_MISMATCH：缓冲区不够，需要按 ReturnLength 重试。</summary>
    internal const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    [DllImport("ntdll.dll")]
    internal static extern int NtQuerySystemInformation(
        int systemInformationClass, IntPtr systemInformation, int systemInformationLength, out int returnLength);

    [DllImport("ntdll.dll")]
    internal static extern int NtQueryObject(
        IntPtr handle, int objectInformationClass, IntPtr objectInformation, int objectInformationLength, out int returnLength);

    // ---------- kernel32 ----------

    /// <summary>PROCESS_DUP_HANDLE：枚举他人句柄表的唯一必需权限。</summary>
    internal const uint ProcessDupHandle = 0x0040;

    /// <summary>PROCESS_QUERY_LIMITED_INFORMATION：取进程映像路径用的最小权限。</summary>
    internal const uint ProcessQueryLimitedInformation = 0x1000;

    internal const uint DuplicateSameAccess = 0x00000002;

    /// <summary>
    /// FILE_TYPE_DISK = 0x0001。注意**不是 2**——2 是 FILE_TYPE_CHAR，写错会把真正的
    /// 磁盘文件/目录全部滤掉、反而只留控制台这类字符设备句柄。
    /// </summary>
    internal const uint FileTypeDisk = 0x0001;

    /// <summary>FILE_FLAG_BACKUP_SEMANTICS：目录也能用 CreateFileW 打开。</summary>
    internal const uint FileFlagBackupSemantics = 0x02000000;

    internal const uint OpenExisting = 3;

    /// <summary>FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE：只探测，不干扰持有者。</summary>
    internal const uint ShareAll = 0x00000007;

    /// <summary>INVALID_HANDLE_VALUE。</summary>
    internal static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool DuplicateHandle(
        IntPtr sourceProcessHandle, IntPtr sourceHandle, IntPtr targetProcessHandle,
        out IntPtr targetHandle, uint desiredAccess, bool inheritHandle, uint options);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetFileType(IntPtr file);

    /// <summary>GetFinalPathNameByHandleW 的 dwFlags：返回盘符形式（\\?\C:\...）。</summary>
    internal const uint VolumeNameDos = 0;

    /// <summary>
    /// 取句柄对应的最终路径。**向文件系统要名字**，不做对象管理器的同步查询。
    /// 取不到（已删除 / 权限受限）返回 0，调用方退回 NtQueryObject 兜底。
    ///
    /// 注意：文件系统查询本身在个别句柄上仍可能卡死（实测某驱动持有的句柄对
    /// GetFinalPathNameByHandleW / GetFileInformationByHandleEx 都挂），
    /// 兜底是 FileLockService 的分片看门狗 + KnownHungObjects 登记。
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint GetFinalPathNameByHandleW(
        IntPtr hFile, System.Text.StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool QueryFullProcessImageNameW(
        IntPtr process, int flags, System.Text.StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int QueryDosDeviceW(string deviceName, System.Text.StringBuilder targetPath, int max);

    // ---------- 结构体 ----------

    /// <summary>
    /// SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX，x64 下每项 40 字节。
    /// SYSTEM_HANDLE_INFORMATION_EX 头部之后紧跟变长的该结构数组。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemHandleTableEntryInfoEx
    {
        public IntPtr Object;
        public UIntPtr UniqueProcessId;
        public UIntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    /// <summary>SYSTEM_HANDLE_INFORMATION_EX 的固定头部：{ NumberOfHandles, Reserved }。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemHandleInformationExHeader
    {
        public UIntPtr NumberOfHandles;
        public UIntPtr Reserved;
    }

    /// <summary>UNICODE_STRING，NtQueryObject(ObjectNameInformation) 的返回载荷。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    // ---------- 常量：本模块的策略性取舍 ----------

    // 这里**没有**任何按 GrantedAccess 整类跳过的常量，这是刻意的：
    // 0x0012019F 并不是「命名管道专属的高危访问」，它就是
    // FILE_GENERIC_READ(0x00120089) | FILE_GENERIC_WRITE(0x00120116) 的展开，也就是
    // 「以读写方式打开一个文件」。Excel / Word / .NET FileStream(ReadWrite) 全都是这个值，
    // 按它整类跳过会把最常见的占用方式静默漏报。
    // 真正需要挡住的命名管道由 FileLockService 里的 GetFileType == FILE_TYPE_DISK 负责
    // （它在 NtQueryObject 之前执行），不依赖 GrantedAccess。

    /// <summary>句柄表查询缓冲起点 64KB，不足时翻倍，上限 64MB（约 160 万句柄，覆盖常见系统）。</summary>
    internal const int InitialHandleBufferSize = 64 * 1024;

    /// <summary>
    /// 上限从 512MB 收紧到 64MB：512MB 的 AllocHGlobal 失败或提交代价本身就是风险，
    /// 而超过 160 万句柄的系统极罕见——真遇到就明确报错，而不是先吃掉半 GB 内存。
    /// </summary>
    internal const int MaxHandleBufferSize = 64 * 1024 * 1024;
}
