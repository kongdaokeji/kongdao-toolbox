namespace TubaWinUi3.Services.HandleCleaner;

/// <summary>句柄清理判定结果。Unknown = 无法判定，不进入清理集合。</summary>
public enum HandleVerdict
{
    Normal,
    Deleted,
    Unknown
}

/// <summary>进程句柄数的分级（用于「疑似泄漏」标注）。</summary>
public enum HandleLeakLevel
{
    Normal,
    Warning,
    Critical
}

/// <summary>
/// 关闭一个句柄的风险分级（深度模式的展示与确认用）：
/// 低 = 指向已删除文件的 File 句柄（文件已不可用，释放残留）；中 = 一般对象（文件/注册表项/进程/线程等）；
/// 高 = 同步对象（事件/互斥体/信号量——关闭可能让目标程序死锁或异常）。
/// </summary>
public enum HandleRisk
{
    Low,
    Medium,
    High
}

/// <summary>强制关闭前对「句柄值是否仍指向扫描到的那个对象」的复核结论。</summary>
internal enum ForceCloseDecision
{
    /// <summary>复核通过，可关闭。</summary>
    Allow,

    /// <summary>句柄值已不在句柄表里（已自行释放）。</summary>
    Gone,

    /// <summary>句柄值已被回收成别的对象（类型/路径对不上）。</summary>
    Recycled,

    /// <summary>无法复核目标（路径解析失败）——保守跳过。</summary>
    Unverifiable
}

/// <summary>
/// 「句柄清理」的安全策略：全部为纯函数，便于单测。
/// 服务层只负责采集事实（DeletePending / 路径是否存在），判定规则集中在这里。
/// </summary>
internal static class HandleCleanerPolicy
{
    /// <summary>疑似泄漏（橙色）阈值：正常桌面进程句柄数远低于此。</summary>
    internal const int LeakWarningHandles = 50_000;

    /// <summary>严重泄漏（红色）阈值。</summary>
    internal const int LeakCriticalHandles = 200_000;

    /// <summary>已删除文件的 NT 路径段：Win10+ 删除待关闭句柄的名字会被移进 $Extend\$Deleted。</summary>
    internal const string DeletedPseudoPathMarker = @"\$Extend\$Deleted\";

    /// <summary>无法转换成盘符的内核设备路径（\Device\…）：不可判定，绝不清理。</summary>
    private const string DevicePathPrefix = @"\Device\";

    /// <summary>
    /// 硬名单：这些进程的句柄**永不清理**（含清理和扫描候选）。
    /// 选取标准：进程死掉会带走整个会话或系统（csrss/wininit/winlogon/smss）、
    /// 或被杀会立刻蓝屏/无法恢复（services/lsass）、或属于内核/内存管理器（System/Registry）。
    /// </summary>
    private static readonly HashSet<string> CriticalProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "Idle",
        "Registry",
        "Secure System",
        "Memory Compression",
        "smss",
        "csrss",
        "wininit",
        "services",
        "lsass",
        "lsaiso",
        "winlogon",
        "fontdrvhost",
        "dwm",
        "MsMpEng",
    };

    internal static bool IsCriticalProcess(int pid, string? processName)
        => pid is 0 or 4
           || (!string.IsNullOrEmpty(processName) && CriticalProcessNames.Contains(processName));

    internal static bool IsSelfProcess(int pid) => pid == Environment.ProcessId;

    internal static HandleLeakLevel ClassifyLeakLevel(int totalHandles)
        => totalHandles >= LeakCriticalHandles ? HandleLeakLevel.Critical
            : totalHandles >= LeakWarningHandles ? HandleLeakLevel.Warning
            : HandleLeakLevel.Normal;

    /// <summary>
    /// 第一层判定：FileStandardInfo 可用时以 DeletePending 为准。
    /// 实测（见 HandleCleanerNative 注释）：删除待关闭的句柄 DeletePending=1，
    /// 正常文件（含 DELETE_ON_CLOSE 但仍在使用）为 0 —— 这是最权威的信号。
    /// </summary>
    internal static HandleVerdict DecideFromStandardInfo(bool standardInfoAvailable, bool deletePending)
        => !standardInfoAvailable ? HandleVerdict.Unknown
            : deletePending ? HandleVerdict.Deleted
            : HandleVerdict.Normal;

    /// <summary>
    /// 第二层判定（第一层失败时的兜底）：按解析出的路径判断。
    /// - 路径含 $Extend\$Deleted → 已删除；
    /// - 纯设备路径（无法转盘符）→ 不可判定；
    /// - 存在性探测：缺失 → 已删除；存在 → 正常；不确定（权限等）→ 不可判定（保守）。
    /// </summary>
    internal static HandleVerdict DecideFromPath(string? resolvedPath, bool? pathMissing)
    {
        if (string.IsNullOrEmpty(resolvedPath)) return HandleVerdict.Unknown;
        if (IsDeletedPseudoPath(resolvedPath)) return HandleVerdict.Deleted;
        if (resolvedPath.StartsWith(DevicePathPrefix, StringComparison.OrdinalIgnoreCase)) return HandleVerdict.Unknown;

        return pathMissing switch
        {
            true => HandleVerdict.Deleted,
            false => HandleVerdict.Normal,
            _ => HandleVerdict.Unknown,
        };
    }

    internal static bool IsDeletedPseudoPath(string path)
        => path.Contains(DeletedPseudoPathMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>内核对象类型名 → 本地化显示名（数据键只在显示层翻译的既有约定）。</summary>
    internal static string TypeLabel(string? kernelTypeName) => kernelTypeName switch
    {
        "File" => LocalizationService.L("HandleCleaner_Type_File", "文件"),
        "Event" => LocalizationService.L("HandleCleaner_Type_Event", "事件"),
        "Mutant" => LocalizationService.L("HandleCleaner_Type_Mutant", "互斥体"),
        "Semaphore" => LocalizationService.L("HandleCleaner_Type_Semaphore", "信号量"),
        "Section" => LocalizationService.L("HandleCleaner_Type_Section", "内存段"),
        "Key" => LocalizationService.L("HandleCleaner_Type_Key", "注册表项"),
        "Process" => LocalizationService.L("HandleCleaner_Type_Process", "进程"),
        "Thread" => LocalizationService.L("HandleCleaner_Type_Thread", "线程"),
        "Token" => LocalizationService.L("HandleCleaner_Type_Token", "令牌"),
        "Timer" => LocalizationService.L("HandleCleaner_Type_Timer", "计时器"),
        "IoCompletion" => LocalizationService.L("HandleCleaner_Type_IoCompletion", "IO 完成端口"),
        _ => LocalizationService.L("HandleCleaner_Type_Other", "其他"),
    };

    // ══════════════════ 深度模式：风险分级 / 访问权限 / 强制关闭复核 ══════════════════

    /// <summary>关闭风险分级。</summary>
    internal static HandleRisk ClassifyRisk(string? kernelTypeName, bool? fileDeletePending) => kernelTypeName switch
    {
        // 已删除文件的句柄是「释放残留」——文件本身已不可用，风险最低。
        "File" => fileDeletePending == true ? HandleRisk.Low : HandleRisk.Medium,
        // 同步对象：关闭可能让目标程序死锁 / 抛异常。
        "Event" or "Mutant" or "Semaphore" => HandleRisk.High,
        _ => HandleRisk.Medium,
    };

    /// <summary>
    /// FILE_* 访问位 → 简短记号（File 类型专用，沿用内核常量名便于对照调试）；
    /// 其他类型只给原始十六进制（通用访问掩码按对象类型展开没有意义）。
    /// </summary>
    private static readonly (uint Bit, string Name)[] FileAccessBits =
    [
        (0x0001, "READ"),
        (0x0002, "WRITE"),
        (0x0004, "APPEND"),
        (0x0010, "READ_EA"),
        (0x0020, "WRITE_EA"),
        (0x0080, "READ_ATTR"),
        (0x0100, "WRITE_ATTR"),
        (0x10000, "DELETE"),
        (0x20000, "READ_CONTROL"),
        (0x40000, "WRITE_DAC"),
        (0x80000, "WRITE_OWNER"),
        (0x100000, "SYNCHRONIZE"),
    ];

    internal static string DescribeHandleAccess(string? kernelTypeName, uint access)
    {
        if (kernelTypeName != "File") return $"0x{access:X8}";

        var parts = new List<string>();
        uint residual = access;
        foreach (var (bit, name) in FileAccessBits)
        {
            if ((access & bit) == 0) continue;
            parts.Add(name);
            residual &= ~bit;
        }

        if (residual != 0) parts.Add($"0x{residual:X}");
        return parts.Count == 0 ? "0x0" : string.Join("|", parts);
    }

    /// <summary>
    /// 强制关闭前的复核（纯函数）：句柄值在扫描到关闭之间可能被回收复用，
    /// 关闭前必须确认它仍指向同一个对象 —— 类型必须一致；File 句柄再比对路径
    /// （路径解析失败时保守拒绝，避免误关已被复用的句柄）。
    /// </summary>
    internal static ForceCloseDecision DecideForceClose(ushort expectedTypeIndex, ushort? currentTypeIndex,
        string? expectedPath, string? currentPath)
    {
        if (currentTypeIndex is null) return ForceCloseDecision.Gone;
        if (currentTypeIndex.Value != expectedTypeIndex) return ForceCloseDecision.Recycled;
        if (string.IsNullOrEmpty(expectedPath)) return ForceCloseDecision.Allow;
        if (string.IsNullOrEmpty(currentPath)) return ForceCloseDecision.Unverifiable;

        return string.Equals(expectedPath, currentPath, StringComparison.OrdinalIgnoreCase)
            ? ForceCloseDecision.Allow
            : ForceCloseDecision.Recycled;
    }

    /// <summary>深度模式也永远不允许碰系统关键进程（服务层的硬护栏）。</summary>
    internal static bool IsForceCloseTargetAllowed(int pid, string? processName)
        => pid > 4 && !IsCriticalProcess(pid, processName) && !IsSelfProcess(pid);
}
