using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.HandleCleaner;

/// <summary>
/// 「句柄清理」的原生互操作声明。
///
/// 设计：只声明本工具**额外需要**的部分；句柄表枚举（SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
/// 的 40 字节布局、缓冲上限、NtQuerySystemInformation/NtQueryObject 等）与
/// OpenProcess/DuplicateHandle/GetFileType/GetFinalPathNameByHandleW 统一复用
/// <see cref="FileLock.FileLockNative"/>（「文件占用查看」已验证过的实现），
/// 避免两处各写一份 ULONG_PTR 宽度的布局定义 —— 那类字段写成 uint 会在 x64 下截断，
/// 产生静默误判。
///
/// 新增部分：
/// - DUPLICATE_CLOSE_SOURCE：关闭**其他进程**句柄的唯一手段（官方文档：
///   https://learn.microsoft.com/windows/win32/api/handleapi/nf-handleapi-duplicatehandle
///   「Closes the source handle. This occurs regardless of any error status returned.」）；
/// - FileStandardInfo / DeletePending：判断句柄指向的文件是否已处于删除态（官方文档：
///   https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfileinformationbyhandleex ）。
///   本机实测（Win11）：DeleteFileW 在句柄仍打开时返回成功，该句柄随即
///   DeletePending=1、NumberOfLinks=0，GetFinalPathNameByHandleW 变成 \$Extend\$Deleted\…；
///   而 FILE_FLAG_DELETE_ON_CLOSE 且仍在正常使用的文件 DeletePending=0（不会误判）；
/// - SeDebugPrivilege 启用：打开 SYSTEM 权限进程的必需权限（官方文档：
///   https://learn.microsoft.com/windows/win32/api/securitybaseapi/nf-securitybaseapi-adjusttokenprivileges ）；
/// - 自建样本句柄：见 HandleCleanerService.TypeSampleSet，用于把 ObjectTypeIndex 映射成类型名。
/// </summary>
internal static class HandleCleanerNative
{
    // ---------- DuplicateHandle 选项 ----------

    /// <summary>DUPLICATE_CLOSE_SOURCE = 0x1：关闭源句柄。</summary>
    internal const uint DuplicateCloseSource = 0x00000001;

    // ---------- 文件信息 ----------

    /// <summary>FILE_INFO_BY_HANDLE_CLASS.FileStandardInfo。</summary>
    internal const int FileStandardInfo = 1;

    /// <summary>
    /// FILE_STANDARD_INFO。DeletePending = 文件已标记删除（最后一个句柄关闭即释放）。
    /// 字段布局按 Win32 头文件：LARGE_INTEGER ×2 + DWORD + BOOLEAN ×2（结构体总长 24）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileStandardInfoData
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        [MarshalAs(UnmanagedType.U1)] public bool DeletePending;
        [MarshalAs(UnmanagedType.U1)] public bool Directory;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetFileInformationByHandleEx(
        IntPtr hFile, int fileInformationClass, ref FileStandardInfoData fileInformation, uint dwBufferSize);

    // ---------- 路径解析 ----------

    /// <summary>GetFinalPathNameByHandleW(VOLUME_NAME_NT)：返回 \Device\HarddiskVolumeN\… 内核路径。</summary>
    internal const uint VolumeNameNt = 0x2;

    /// <summary>Win32 错误码：存在性探测里视为「文件/路径已缺失」。</summary>
    internal const int ErrorFileNotFound = 2;
    internal const int ErrorPathNotFound = 3;
    internal const int ErrorInvalidName = 123;

    /// <summary>ERROR_INVALID_HANDLE：句柄已经在持有者那边关闭（自愈）。</summary>
    internal const int ErrorInvalidHandle = 6;

    // ---------- 自建样本句柄（类型索引 → 名称映射） ----------

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateMutexW(IntPtr lpMutexAttributes, bool bInitialOwner, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateSemaphoreW(IntPtr lpSemaphoreAttributes, int lInitialCount, int lMaximumCount, string? lpName);

    /// <summary>CreateFileMappingW(INVALID_HANDLE_VALUE, …)：匿名内存段（Section 类型）。</summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateFileMappingW(IntPtr hFile, IntPtr lpFileMappingAttributes, uint flProtect,
        uint dwMaximumSizeHigh, uint dwMaximumSizeLow, string? lpName);

    internal const uint PageReadWrite = 0x04;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateWaitableTimerW(IntPtr lpTimerAttributes, bool bManualReset, string? lpTimerName);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateIoCompletionPort(IntPtr fileHandle, IntPtr existingCompletionPort,
        UIntPtr completionKey, uint numberOfConcurrentThreads);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);

    /// <summary>THREAD_QUERY_LIMITED_INFORMATION：取当前线程句柄做样本的最小权限。</summary>
    internal const uint ThreadQueryLimitedInformation = 0x0800;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int RegOpenKeyExW(IntPtr hKey, string lpSubKey, uint ulOptions, uint samDesired, out IntPtr phkResult);

    internal static readonly IntPtr HKeyLocalMachine = new(unchecked((int)0x80000002));
    internal const uint KeyRead = 0x20019;

    // ---------- 令牌与 SeDebugPrivilege ----------

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    internal const uint TokenQuery = 0x0008;
    internal const uint TokenAdjustPrivileges = 0x0020;
    internal const uint SePrivilegeEnabled = 0x00000002;

    /// <summary>AdjustTokenPrivileges 返回 ERROR_NOT_ALL_ASSIGNED：并非所有权限都成功分配（必须检查）。</summary>
    internal const int ErrorNotAllAssigned = 1300;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    /// <summary>TOKEN_PRIVILEGES 的单权限变体（PrivilegeCount = 1）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool LookupPrivilegeValueW(string? lpSystemName, string lpName, out Luid lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges,
        ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);
}
