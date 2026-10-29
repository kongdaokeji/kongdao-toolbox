using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TubaWinUi3.Services.FileLock;

namespace TubaWinUi3.Services.HandleCleaner;

/// <summary>一个进程的句柄占用摘要（进程句柄排行的数据行）。</summary>
public sealed class ProcessHandleSummary
{
    public int ProcessId { get; init; }
    public string Name { get; init; } = "";
    public string ImagePath { get; init; } = "";
    public DateTime? StartTimeUtc { get; init; }
    public int TotalHandles { get; init; }
    public int FileHandles { get; init; }

    /// <summary>false = 系统关键进程 / 无法清理（行上标「受保护」）。</summary>
    public bool CleaningAllowed { get; init; }

    public HandleLeakLevel LeakLevel { get; init; }

    /// <summary>主要句柄类型（已翻译显示名，降序，最多 5 项）。</summary>
    public IReadOnlyList<KeyValuePair<string, int>> TopTypes { get; init; } = [];
}

/// <summary>系统句柄总览（一次句柄表快照即可得到，不做任何逐句柄原生查询）。</summary>
public sealed class HandleCleanerOverview
{
    public long TotalHandles { get; init; }
    public int ProcessCount { get; init; }
    public int SuspectedLeakCount { get; init; }
    public int FileHandles { get; init; }
    public DateTime ScannedUtc { get; init; }
    public IReadOnlyList<ProcessHandleSummary> Processes { get; init; } = [];
}

/// <summary>一个被判定为「指向已删除文件」的句柄（HandleValue 仅用于关闭，不参与展示）。</summary>
public sealed class CandidateHandle
{
    public ulong HandleValue { get; init; }
    public string DisplayPath { get; init; } = "";
}

/// <summary>候选句柄按进程分组（清理请求的单位）。</summary>
public sealed class CandidateProcessGroup
{
    public int ProcessId { get; init; }
    public string Name { get; init; } = "";
    public string ImagePath { get; init; } = "";
    public DateTime? StartTimeUtc { get; init; }
    public List<CandidateHandle> Handles { get; init; } = new();

    /// <summary>该进程里无法判定的文件句柄数（展示用，不清理）。</summary>
    public int UnknownCount { get; set; }
}

/// <summary>失效句柄扫描结果。</summary>
public sealed class CandidateScanResult
{
    public IReadOnlyList<CandidateProcessGroup> Groups { get; init; } = [];
    public int ScannedFileHandles { get; init; }
    public int SkippedProcesses { get; init; }
    public int UnreadableHandles { get; init; }
    public int GuardedHandles { get; init; }
    public bool Truncated { get; init; }
    public bool FileTypeIndexUnavailable { get; init; }
    public TimeSpan Elapsed { get; init; }

    public int TotalCandidates => Groups.Sum(g => g.Handles.Count);
    public int TotalUnknown => Groups.Sum(g => g.UnknownCount);
    public int AffectedProcessCount => Groups.Count(g => g.Handles.Count > 0);
}

/// <summary>安全清理结果。</summary>
public sealed class CleanOutcome
{
    public int Freed { get; init; }
    public int AlreadyGone { get; init; }
    public int AccessDenied { get; init; }
    public int ProcessGone { get; init; }
    public int OtherFailures { get; init; }
    public int SkippedType { get; init; }
    public bool TimedOut { get; init; }
    public IReadOnlyList<string> FailureSamples { get; init; } = [];

    internal Exception? Error { get; init; }
}

// ══════════════════ 深度模式模型 ══════════════════

/// <summary>深度模式：进程里一个句柄的完整描述（枚举时尽力解析对象名）。</summary>
public sealed class ProcessHandleInfo
{
    public ulong HandleValue { get; init; }
    public ushort TypeIndex { get; init; }

    /// <summary>内核对象类型名（File / Event / Mutant / …，未知为 "Other"）。</summary>
    public string TypeName { get; init; } = "";

    /// <summary>对象名（文件为盘符路径；其他类型尽力而为，可能为 null）。</summary>
    public string? ObjectName { get; init; }

    public uint GrantedAccess { get; init; }
    public HandleRisk Risk { get; init; }

    /// <summary>File 句柄且 DeletePending（指向已删除文件）。</summary>
    public bool IsDeletedFile { get; init; }

    public string AccessText => HandleCleanerPolicy.DescribeHandleAccess(TypeName, GrantedAccess);
}

/// <summary>某类型的句柄数量（句柄浏览器的类型筛选下拉用）。</summary>
public sealed class ProcessHandleTypeCount
{
    public ushort TypeIndex { get; init; }
    public string TypeName { get; init; } = "";
    public int Count { get; init; }
}

/// <summary>进程句柄枚举结果。</summary>
public sealed class ProcessHandleListResult
{
    public IReadOnlyList<ProcessHandleInfo> Handles { get; init; } = [];

    /// <summary>该进程全部句柄的类型分布（不受类型筛选与数量上限影响）。</summary>
    public IReadOnlyList<ProcessHandleTypeCount> TypeCounts { get; init; } = [];

    public int TotalInProcess { get; init; }
    public bool Truncated { get; init; }
    public int GuardedHandles { get; init; }
    public int SkippedProcesses { get; init; }
    public bool FileTypeIndexUnavailable { get; init; }
    public TimeSpan Elapsed { get; init; }
}

/// <summary>按路径扫描：一个进程持有目标路径的一个句柄。</summary>
public sealed class PathHandleItem
{
    public ulong HandleValue { get; init; }
    public ushort TypeIndex { get; init; }
    public string DisplayPath { get; init; } = "";
}

/// <summary>按路径扫描：按进程分组的持有句柄。</summary>
public sealed class PathHandleGroup
{
    public int ProcessId { get; init; }
    public string Name { get; init; } = "";
    public string ImagePath { get; init; } = "";
    public DateTime? StartTimeUtc { get; init; }
    public List<PathHandleItem> Handles { get; init; } = new();
}

public enum PathHandleScanError
{
    None,
    EmptyPath,
    NotFound,
    ResolveFailed,
    ObjectTypeUnavailable
}

/// <summary>按路径扫描结果（「解除文件占用」）。</summary>
public sealed class PathHandleScanResult
{
    public IReadOnlyList<PathHandleGroup> Groups { get; init; } = [];
    public PathHandleScanError Error { get; init; }
    public string? ErrorDetail { get; init; }
    public bool IsDirectoryTarget { get; init; }
    public int ScannedHandles { get; init; }
    public int SkippedProcesses { get; init; }
    public int GuardedHandles { get; init; }
    public bool Truncated { get; init; }
    public TimeSpan Elapsed { get; init; }

    public int TotalHandles => Groups.Sum(g => g.Handles.Count);
    public int AffectedProcessCount => Groups.Count(g => g.Handles.Count > 0);
}

/// <summary>强制关闭请求里的一个句柄（携带扫描时的类型与路径，供关闭前复核）。</summary>
public sealed class ForceCloseHandleRequest
{
    public ulong HandleValue { get; init; }
    public ushort ExpectedTypeIndex { get; init; }
    public bool IsFile { get; init; }

    /// <summary>File 句柄的路径（复核对象未被回收的依据）；无法解析时为 null。</summary>
    public string? ExpectedPath { get; init; }
}

/// <summary>强制关闭请求按进程分组。</summary>
public sealed class ForceCloseGroup
{
    public int ProcessId { get; init; }
    public string Name { get; init; } = "";
    public string ImagePath { get; init; } = "";
    public DateTime? StartTimeUtc { get; init; }
    public List<ForceCloseHandleRequest> Handles { get; init; } = new();
}

/// <summary>强制关闭结果。</summary>
public sealed class ForceCloseOutcome
{
    public int Freed { get; init; }
    public int AlreadyGone { get; init; }

    /// <summary>句柄值已被回收成别的对象，复核不过而跳过（防误关的关键保护）。</summary>
    public int RecycledSkipped { get; init; }

    /// <summary>无法复核目标（路径解析失败），保守跳过。</summary>
    public int Unverifiable { get; init; }

    public int AccessDenied { get; init; }
    public int ProcessGone { get; init; }

    /// <summary>系统关键进程的保护性跳过（硬护栏）。</summary>
    public int ProtectedSkipped { get; init; }

    public int OtherFailures { get; init; }
    public bool Truncated { get; init; }
    public IReadOnlyList<string> FailureSamples { get; init; } = [];

    internal Exception? Error { get; init; }
}

/// <summary>
/// 「句柄清理」核心服务。
///
/// 两种能力：
/// 1) 安全清理：只处理「指向已删除文件」的 File 句柄（DeletePending 为准，路径兜底），
///    关键进程硬名单 + 清理前身份/失效双重校验；
/// 2) 深度模式：枚举任意进程的全部句柄（类型/对象名/访问权限/风险分级），
///    按路径查找全系统持有句柄并强制关闭（关闭前做「类型 + 路径」复核防句柄复用误关），
///    支持任意类型句柄的定向关闭 —— 更自由，但仍然永不触碰系统关键进程。
///
/// 防假死：所有逐句柄操作都跑在「分片线程 + 看门狗」引擎里（见 ShardRunner）：
/// NtQueryObject / 文件系统查询在极少数句柄上会长时间阻塞，卡住的分片会被放弃并续跑，
/// 卡死的 (pid, 句柄) 会被登记、后续扫描直接跳过（否则每次跳过都会泄漏一个副本句柄，
/// 下次扫描又变成新的卡死项）。不调用 TerminateThread。
/// </summary>
public static class HandleCleanerService
{
    // ---------- 看门狗参数（与 FileLockService 保持同一量级） ----------
    private const int WatchdogTickMs = 200;
    private const int StallTimeoutMs = 8000;
    private const int IdleAbandonMs = 2000;
    private const int MaxStallRecoveries = 16;
    private const int HardTimeoutMs = 60_000;

    /// <summary>关闭类批量操作的硬超时（候选集大时关闭本身要花时间，给更宽的预算）。</summary>
    private const int CloseHardTimeoutMs = 120_000;

    private const int MaxFailureSamples = 10;

    /// <summary>单进程句柄枚举的数量上限（一次展示/勾选的上限；可换类型筛选继续）。</summary>
    internal const int MaxEnumerateHandles = 100_000;

    private static int _activeScanRunners;

    /// <summary>仍在跑的扫描分片数（测试用：扫描结束后应归零）。</summary>
    internal static int ActiveScanRunners => Volatile.Read(ref _activeScanRunners);

    /// <summary>
    /// 已知会卡死的句柄（pid + 句柄值）。登记后后续扫描在复制/查询之前直接跳过。
    /// </summary>
    private static readonly ConcurrentDictionary<(int Pid, ulong Handle), byte> KnownHungHandles = new();
    private const int MaxKnownHungHandles = 256;

    /// <summary>深度模式的「强制关闭」是否跳过已知卡死句柄：false —— 用户明确要求关闭时仍尝试（复核挂起由看门狗兜底）。</summary>
    private const bool SkipKnownHungForForceClose = false;

    // ══════════════════ 概览扫描 ══════════════════

    public static Task<HandleCleanerOverview> ScanOverviewAsync(CancellationToken cancellationToken)
        => Task.Run(() => ScanOverview(cancellationToken), cancellationToken);

    /// <summary>
    /// 单次句柄表快照 → 系统总数 / 每进程 总句柄+文件句柄+类型分布 / 疑似泄漏分级。
    /// 全部为纯整数比较，不做任何逐句柄的原生查询（毫秒级）。
    /// </summary>
    public static HandleCleanerOverview ScanOverview(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var processInfo = new ProcessInfoCache();
        using var samples = TypeSampleSet.Create();
        IntPtr buffer = QueryHandleTable(out int count);

        try
        {
            var typeIndexes = ResolveSampleTypeIndexes(samples, buffer, count);
            int fileIndex = typeIndexes.TryGetValue("File", out ushort fi) ? fi : 0;

            var perPid = new Dictionary<int, ProcessAccumulator>();
            int entrySize = Marshal.SizeOf<FileLockNative.SystemHandleTableEntryInfoEx>();
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = Marshal.PtrToStructure<FileLockNative.SystemHandleTableEntryInfoEx>(
                    EntryPointer(buffer, i, entrySize));

                ulong rawPid = entry.UniqueProcessId.ToUInt64();
                if (rawPid == 0 || rawPid > int.MaxValue) continue;
                int pid = (int)rawPid;

                if (!perPid.TryGetValue(pid, out var accumulator))
                {
                    accumulator = new ProcessAccumulator();
                    perPid[pid] = accumulator;
                }

                accumulator.Total++;
                if (entry.ObjectTypeIndex == fileIndex) accumulator.File++;
                accumulator.Types.TryGetValue(entry.ObjectTypeIndex, out int perType);
                accumulator.Types[entry.ObjectTypeIndex] = perType + 1;
            }

            var rows = new List<ProcessHandleSummary>(perPid.Count);
            long totalHandles = 0;
            int fileHandles = 0;
            int suspected = 0;
            int selfPid = Environment.ProcessId;

            foreach (var (pid, accumulator) in perPid)
            {
                totalHandles += accumulator.Total;
                fileHandles += accumulator.File;
                if (pid == selfPid) continue;

                var (name, path, startUtc) = processInfo.Get(pid);
                bool allowed = pid > 4 && !HandleCleanerPolicy.IsCriticalProcess(pid, name);
                var level = HandleCleanerPolicy.ClassifyLeakLevel(accumulator.Total);
                if (allowed && level != HandleLeakLevel.Normal) suspected++;

                rows.Add(new ProcessHandleSummary
                {
                    ProcessId = pid,
                    Name = name,
                    ImagePath = path,
                    StartTimeUtc = startUtc,
                    TotalHandles = accumulator.Total,
                    FileHandles = accumulator.File,
                    CleaningAllowed = allowed,
                    LeakLevel = level,
                    TopTypes = BuildTopTypes(accumulator.Types, typeIndexes),
                });
            }

            rows.Sort((a, b) =>
            {
                int byCount = b.TotalHandles.CompareTo(a.TotalHandles);
                return byCount != 0 ? byCount : a.ProcessId.CompareTo(b.ProcessId);
            });

            return new HandleCleanerOverview
            {
                TotalHandles = totalHandles,
                ProcessCount = rows.Count,
                SuspectedLeakCount = suspected,
                FileHandles = fileHandles,
                ScannedUtc = DateTime.UtcNow,
                Processes = rows,
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<KeyValuePair<string, int>> BuildTopTypes(Dictionary<ushort, int> types,
        Dictionary<string, ushort> typeIndexes)
    {
        // 类型索引 → 名称依赖本次快照里的样本句柄；只负责聚合排序与显示名翻译。
        var indexToName = new Dictionary<ushort, string>();
        foreach (var (name, index) in typeIndexes) indexToName[index] = name;

        var named = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (index, count) in types)
        {
            string name = indexToName.TryGetValue(index, out var known) ? known : "Other";
            named.TryGetValue(name, out int existing);
            named[name] = existing + count;
        }

        return named
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(5)
            .Select(kv => new KeyValuePair<string, int>(HandleCleanerPolicy.TypeLabel(kv.Key), kv.Value))
            .ToList();
    }

    // ══════════════════ 安全清理：候选扫描 ══════════════════

    public static Task<CandidateScanResult> ScanCandidatesAsync(int? processFilterPid, CancellationToken cancellationToken,
        IProgress<string>? progress = null)
        => Task.Run(() => ScanCandidates(processFilterPid, cancellationToken, progress), cancellationToken);

    /// <summary>
    /// 扫描非关键进程的文件句柄，找出指向已删除文件的失效句柄。
    ///
    /// 判定顺序（任一层的结论即可定案）：
    /// 1) GetFileInformationByHandleEx(FileStandardInfo).DeletePending —— 实测权威信号；
    /// 2) 解析路径（DOS → NT → NtQueryObject）后：$Extend\$Deleted → 已删除；
    ///    存在性探测缺失 → 已删除；存在 → 正常；探测不确定（权限等）→ 无法判定；
    ///    纯设备路径（无法转盘符）→ 无法判定。
    ///
    /// 无法判定的句柄一律不进入清理集合。
    /// </summary>
    public static CandidateScanResult ScanCandidates(int? processFilterPid, CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        var stopwatch = Stopwatch.StartNew();
        TryEnableDebugPrivilege();
        var processInfo = new ProcessInfoCache();
        using var samples = TypeSampleSet.Create();
        IntPtr buffer = QueryHandleTable(out int count);

        List<ScanItem<object?>> work;
        try
        {
            var typeIndexes = ResolveSampleTypeIndexes(samples, buffer, count);

            if (!typeIndexes.TryGetValue("File", out ushort fileIndex) || fileIndex == 0)
            {
                return new CandidateScanResult { FileTypeIndexUnavailable = true, Elapsed = stopwatch.Elapsed };
            }

            progress?.Report(LocalizationService.L("HandleCleaner_ScanningPhase", "正在扫描各进程的文件句柄…"));

            work = new List<ScanItem<object?>>();
            int entrySize = Marshal.SizeOf<FileLockNative.SystemHandleTableEntryInfoEx>();
            ulong selfPid = (ulong)Environment.ProcessId;

            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = Marshal.PtrToStructure<FileLockNative.SystemHandleTableEntryInfoEx>(
                    EntryPointer(buffer, i, entrySize));

                if (entry.ObjectTypeIndex != fileIndex) continue;

                ulong rawPid = entry.UniqueProcessId.ToUInt64();
                if (rawPid <= 4 || rawPid == selfPid || rawPid > int.MaxValue) continue;
                int pid = (int)rawPid;

                if (processFilterPid is not null && pid != processFilterPid.Value) continue;

                var (name, _, _) = processInfo.Get(pid);
                if (HandleCleanerPolicy.IsCriticalProcess(pid, name)) continue;

                work.Add(new ScanItem<object?>(pid, entry.HandleValue.ToUInt64(), entry.ObjectTypeIndex,
                    entry.GrantedAccess, null));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        var collector = new CandidateCollector(processInfo);
        int unreadable = 0;
        var result = RunShard(work, (item, process, local, error) =>
        {
            if (local == IntPtr.Zero)
            {
                Interlocked.Increment(ref unreadable);
                return;
            }

            if (FileLockNative.GetFileType(local) != FileLockNative.FileTypeDisk) return;

            var verdict = ClassifyHandle(local, out string? displayPath);
            if (verdict == HandleVerdict.Deleted)
                collector.AddCandidate(item.Pid, item.HandleValue, displayPath ?? "");
            else if (verdict == HandleVerdict.Unknown)
                collector.AddUnknown(item.Pid);
        }, skipKnownHung: true, HardTimeoutMs, progressEvery: 0x3FF, cancellationToken, progress,
            "HandleCleaner_ProgressFormat", "已检查 {0} / {1} 个文件句柄");

        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
        if (result.Error is not null) throw new InvalidOperationException(result.Error.Message, result.Error);

        return new CandidateScanResult
        {
            Groups = collector.Build(),
            ScannedFileHandles = result.Processed,
            SkippedProcesses = result.SkippedProcesses,
            UnreadableHandles = unreadable,
            GuardedHandles = result.GuardedHandles,
            Truncated = result.Truncated,
            Elapsed = stopwatch.Elapsed,
        };
    }

    // ══════════════════ 安全清理：关闭 ══════════════════

    public static Task<CleanOutcome> CleanAsync(IReadOnlyList<CandidateProcessGroup> groups,
        IProgress<string>? progress = null)
        => Task.Run(() => Clean(groups, progress));

    /// <summary>
    /// 安全清理：身份核对 → 逐句柄二次判定（仍指向已删除文件）→ 立即关闭源句柄。
    /// 关闭手段：DuplicateHandle + DUPLICATE_CLOSE_SOURCE（官方文档语义：
    /// 无论成败都会尝试关闭源句柄）。无法判定的句柄不在这里 —— 它们从未进入候选集合。
    /// </summary>
    public static CleanOutcome Clean(IReadOnlyList<CandidateProcessGroup> groups, IProgress<string>? progress = null)
    {
        TryEnableDebugPrivilege();
        var collector = new CleanCollector();
        var work = new List<ScanItem<object?>>();

        foreach (var group in groups)
        {
            // 身份核对：确认 PID 仍指向扫描时的那个进程（期间进程可能退出、PID 可能被复用）。
            if (!HandleCleanerPolicy.IsForceCloseTargetAllowed(group.ProcessId, group.Name))
            {
                collector.AddAccessDenied(group.Handles.Count);
                continue;
            }
            if (FileLockService.VerifyProcessIdentity(group.ProcessId, group.Name, group.ImagePath, group.StartTimeUtc)
                != ProcessIdentityCheck.Match)
            {
                collector.AddProcessGone(group.Handles.Count);
                continue;
            }

            foreach (var handle in group.Handles)
                work.Add(new ScanItem<object?>(group.ProcessId, handle.HandleValue, 0, 0, null));
        }

        progress?.Report(LocalizationService.L("HandleCleaner_CleaningPhase", "正在清理失效句柄…"));
        var result = RunShard(work, (item, process, local, error) =>
            CloseIfStillDeleted(item, process, local, error, collector),
            skipKnownHung: false, CloseHardTimeoutMs, progressEvery: 0x3F, CancellationToken.None, progress,
            "HandleCleaner_CleanProgressFormat", "正在清理句柄 {0} / {1}");

        if (result.Error is not null) throw new InvalidOperationException(result.Error.Message, result.Error);
        return collector.Build(result.Truncated);
    }

    /// <summary>
    /// 关闭一个候选句柄：先复制一份做即时二次判定（仍指向已删除文件才关），
    /// 再用 DUPLICATE_CLOSE_SOURCE 关闭源句柄。扫描到关闭之间句柄值可能被回收复用 ——
    /// 二次判定把窗口压到最小，这是同类远程关闭方案（Sysinternals handle.exe 等）共有的取舍。
    /// </summary>
    private static void CloseIfStillDeleted(ScanItem<object?> item, IntPtr process, IntPtr local, int error,
        CleanCollector collector)
    {
        if (process == IntPtr.Zero)
        {
            collector.AccessDenied();
            return;
        }
        if (local == IntPtr.Zero)
        {
            if (error == HandleCleanerNative.ErrorInvalidHandle) collector.AlreadyGone();
            else collector.AccessDenied();
            return;
        }

        if (FileLockNative.GetFileType(local) != FileLockNative.FileTypeDisk)
        {
            collector.SkippedType();
            return;
        }
        if (ClassifyHandle(local, out _) != HandleVerdict.Deleted)
        {
            collector.AlreadyGone();
            return;
        }

        CloseSourceHandle(item, process, collector);
    }

    /// <summary>用 DUPLICATE_CLOSE_SOURCE 关闭源句柄（返回的副本立即关掉），并分类失败原因。</summary>
    private static void CloseSourceHandle(ScanItem<object?> item, IntPtr process, CleanCollector collector)
    {
        IntPtr source = new(unchecked((long)item.HandleValue));
        IntPtr current = FileLockNative.GetCurrentProcess();

        if (FileLockNative.DuplicateHandle(process, source, current, out IntPtr closed, 0, false,
                HandleCleanerNative.DuplicateCloseSource | FileLockNative.DuplicateSameAccess))
        {
            FileLockNative.CloseHandle(closed);
            collector.Freed();
            return;
        }

        int error = Marshal.GetLastWin32Error();
        if (error == HandleCleanerNative.ErrorInvalidHandle) collector.AlreadyGone();
        else collector.AddFailure(item.Pid, "", item.HandleValue, error);
    }

    // ══════════════════ 深度模式：枚举进程句柄 ══════════════════

    public static Task<ProcessHandleListResult> EnumerateHandlesAsync(int pid, ushort? typeIndexFilter,
        CancellationToken cancellationToken, IProgress<string>? progress = null)
        => Task.Run(() => EnumerateHandles(pid, typeIndexFilter, cancellationToken, progress), cancellationToken);

    /// <summary>
    /// 枚举一个进程的全部句柄（或指定类型），尽力解析对象名：
    /// - File：GetFinalPathNameByHandleW（已删除文件会解析为 $Extend\$Deleted\…）+ DeletePending 判定风险；
    /// - 其他类型：NtQueryObject(ObjectNameInformation)（可能拿不到名字，返回 null）。
    /// 保护性进程（System 等）也允许**查看**（关闭由 ForceClose 硬护栏拒绝）。
    /// </summary>
    public static ProcessHandleListResult EnumerateHandles(int pid, ushort? typeIndexFilter,
        CancellationToken cancellationToken, IProgress<string>? progress = null)
    {
        var stopwatch = Stopwatch.StartNew();
        TryEnableDebugPrivilege();
        var processInfo = new ProcessInfoCache();
        using var samples = TypeSampleSet.Create();
        IntPtr buffer = QueryHandleTable(out int count);

        var typeCounts = new Dictionary<ushort, int>();
        List<ScanItem<object?>> work;
        bool truncated = false;
        int fileIndex;
        Dictionary<string, ushort> typeIndexes;
        int totalInProcess = 0;

        try
        {
            typeIndexes = ResolveSampleTypeIndexes(samples, buffer, count);
            // File 样本创建失败不影响其他类型的枚举，只是 File 行没有名称/删除态。
            fileIndex = typeIndexes.TryGetValue("File", out ushort resolvedFileIndex) ? resolvedFileIndex : 0;

            work = new List<ScanItem<object?>>();
            int entrySize = Marshal.SizeOf<FileLockNative.SystemHandleTableEntryInfoEx>();

            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = Marshal.PtrToStructure<FileLockNative.SystemHandleTableEntryInfoEx>(
                    EntryPointer(buffer, i, entrySize));

                if (entry.UniqueProcessId.ToUInt64() != (ulong)pid) continue;

                totalInProcess++;
                typeCounts.TryGetValue(entry.ObjectTypeIndex, out int perType);
                typeCounts[entry.ObjectTypeIndex] = perType + 1;

                if (typeIndexFilter is not null && entry.ObjectTypeIndex != typeIndexFilter.Value) continue;

                if (work.Count >= MaxEnumerateHandles)
                {
                    truncated = true;
                    continue;
                }

                work.Add(new ScanItem<object?>(pid, entry.HandleValue.ToUInt64(), entry.ObjectTypeIndex,
                    entry.GrantedAccess, null));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        var indexToName = new Dictionary<ushort, string>();
        foreach (var (name, index) in typeIndexes) indexToName[index] = name;

        progress?.Report(string.Format(
            LocalizationService.L("HandleCleaner_EnumPhase", "正在读取 {0} 的句柄…"), processInfo.Get(pid).Name));

        var handles = new List<ProcessHandleInfo>(work.Count);
        var listLock = new object();

        var result = RunShard(work, (item, process, local, error) =>
        {
            string typeName = indexToName.TryGetValue(item.TypeIndex, out var known) ? known : "Other";

            if (local == IntPtr.Zero)
            {
                // 打不开/复制不了也如实列出（类型与访问权限来自句柄表，足够定位问题）。
                lock (listLock)
                    handles.Add(DescribeHandle(item, typeName, objectName: null, isDeleted: false));
                return;
            }

            string? objectName = null;
            bool isDeleted = false;

            if (item.TypeIndex == fileIndex)
            {
                objectName = TryResolvePathLight(local);

                var info = default(HandleCleanerNative.FileStandardInfoData);
                if (HandleCleanerNative.GetFileInformationByHandleEx(local, HandleCleanerNative.FileStandardInfo,
                        ref info, (uint)Marshal.SizeOf<HandleCleanerNative.FileStandardInfoData>()))
                {
                    isDeleted = info.DeletePending;
                }
            }
            else
            {
                objectName = QueryObjectName(local);
            }

            lock (listLock)
                handles.Add(DescribeHandle(item, typeName, objectName, isDeleted));
        }, skipKnownHung: true, HardTimeoutMs, progressEvery: 0x3FF, cancellationToken, progress,
            "HandleCleaner_EnumProgressFormat", "已读取 {0} / {1} 个句柄");

        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
        if (result.Error is not null) throw new InvalidOperationException(result.Error.Message, result.Error);

        handles.Sort((a, b) =>
        {
            int byType = string.CompareOrdinal(a.TypeName, b.TypeName);
            if (byType != 0) return byType;
            int byName = string.Compare(a.ObjectName, b.ObjectName, StringComparison.OrdinalIgnoreCase);
            if (byName != 0) return byName;
            return a.HandleValue.CompareTo(b.HandleValue);
        });

        var counts = typeCounts
            .Select(kv => new ProcessHandleTypeCount
            {
                TypeIndex = kv.Key,
                TypeName = indexToName.TryGetValue(kv.Key, out var known) ? known : "Other",
                Count = kv.Value,
            })
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.TypeName, StringComparer.Ordinal)
            .ToList();

        return new ProcessHandleListResult
        {
            Handles = handles,
            TypeCounts = counts,
            TotalInProcess = totalInProcess,
            Truncated = truncated || result.Truncated,
            GuardedHandles = result.GuardedHandles,
            SkippedProcesses = result.SkippedProcesses,
            FileTypeIndexUnavailable = fileIndex == 0,
            Elapsed = stopwatch.Elapsed,
        };
    }

    private static ProcessHandleInfo DescribeHandle(ScanItem<object?> item, string typeName, string? objectName,
        bool isDeleted)
        => new()
        {
            HandleValue = item.HandleValue,
            TypeIndex = item.TypeIndex,
            TypeName = typeName,
            ObjectName = objectName,
            GrantedAccess = item.GrantedAccess,
            Risk = HandleCleanerPolicy.ClassifyRisk(typeName, typeName == "File" ? isDeleted : null),
            IsDeletedFile = isDeleted,
        };

    // ══════════════════ 深度模式：按路径查找占用 ══════════════════

    public static Task<PathHandleScanResult> FindHandlesForPathAsync(string path, int? processFilterPid,
        CancellationToken cancellationToken, IProgress<string>? progress = null)
        => Task.Run(() => FindHandlesForPath(path, processFilterPid, cancellationToken, progress), cancellationToken);

    /// <summary>
    /// 「解除文件占用」：扫描全系统（或指定进程）中指向目标路径的 File 句柄，返回具体句柄值。
    /// 匹配规则与「文件占用查看」一致（文件完全相等 / 目录前缀 + 分隔符；final path 为主、
    /// 内核设备名为兜底），额外把每个句柄的值带回，供强制关闭使用。
    /// </summary>
    public static PathHandleScanResult FindHandlesForPath(string path, int? processFilterPid,
        CancellationToken cancellationToken, IProgress<string>? progress = null)
    {
        var stopwatch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(path))
            return new PathHandleScanResult { Error = PathHandleScanError.EmptyPath };

        string raw = path.Trim();
        bool isDirectory = Directory.Exists(raw);
        if (!isDirectory && !File.Exists(raw))
            return new PathHandleScanResult { Error = PathHandleScanError.NotFound };

        TryEnableDebugPrivilege();
        var processInfo = new ProcessInfoCache();
        using var samples = TypeSampleSet.Create();

        string targetFinalPath = NormalizeTargetPath(raw);
        string targetKernelName;
        ushort fileIndex;

        // 目标自身开一个句柄：拿内核设备名兜底匹配名 + 确认 File 类型（沿用 FileLockService 的思路）。
        IntPtr ownHandle = FileLockNative.CreateFileW(raw, 0, FileLockNative.ShareAll, IntPtr.Zero,
            FileLockNative.OpenExisting, FileLockNative.FileFlagBackupSemantics, IntPtr.Zero);
        if (ownHandle == FileLockNative.InvalidHandleValue)
        {
            return new PathHandleScanResult
            {
                Error = PathHandleScanError.ResolveFailed,
                ErrorDetail = $"CreateFileW 失败（错误码 {Marshal.GetLastWin32Error()}）",
            };
        }

        try
        {
            targetKernelName = QueryObjectName(ownHandle) ?? "";
        }
        finally
        {
            FileLockNative.CloseHandle(ownHandle);
        }

        IntPtr buffer = QueryHandleTable(out int count);
        List<ScanItem<object?>> work;
        try
        {
            var typeIndexes = ResolveSampleTypeIndexes(samples, buffer, count);
            if (!typeIndexes.TryGetValue("File", out fileIndex) || fileIndex == 0)
                return new PathHandleScanResult { Error = PathHandleScanError.ObjectTypeUnavailable };
            progress?.Report(LocalizationService.L("HandleCleaner_UnlockScanningPhase", "正在扫描占用该路径的句柄…"));

            work = new List<ScanItem<object?>>();
            int entrySize = Marshal.SizeOf<FileLockNative.SystemHandleTableEntryInfoEx>();
            ulong selfPid = (ulong)Environment.ProcessId;

            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = Marshal.PtrToStructure<FileLockNative.SystemHandleTableEntryInfoEx>(
                    EntryPointer(buffer, i, entrySize));

                if (entry.ObjectTypeIndex != fileIndex) continue;

                ulong rawPid = entry.UniqueProcessId.ToUInt64();
                if (rawPid <= 4 || rawPid == selfPid || rawPid > int.MaxValue) continue;
                int pid = (int)rawPid;

                if (processFilterPid is not null && pid != processFilterPid.Value) continue;

                var (name, _, _) = processInfo.Get(pid);
                if (HandleCleanerPolicy.IsCriticalProcess(pid, name)) continue;

                work.Add(new ScanItem<object?>(pid, entry.HandleValue.ToUInt64(), entry.ObjectTypeIndex,
                    entry.GrantedAccess, null));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        var collector = new PathMatchCollector(processInfo);
        var deviceMap = BuildDeviceMap();

        var result = RunShard(work, (item, process, local, error) =>
        {
            if (local == IntPtr.Zero) return;
            if (FileLockNative.GetFileType(local) != FileLockNative.FileTypeDisk) return;

            string? display = null;
            string? kernelName = null;

            string? dos = QueryFinalPath(local, FileLockNative.VolumeNameDos);
            if (dos is not null)
            {
                display = FileLockService.NormalizeFinalPath(dos);
            }
            else
            {
                string? nt = QueryFinalPath(local, HandleCleanerNative.VolumeNameNt);
                if (nt is not null) kernelName = nt;
            }

            // 主匹配：final path（盘符路径）对目标 final path。
            if (display is not null)
            {
                if (FileLockService.MatchesTargetPath(targetFinalPath, display, isDirectory))
                {
                    collector.Add(item.Pid, item.HandleValue, item.TypeIndex, display);
                }
                return;
            }

            // 兜底匹配：内核设备名对目标内核名（final path 解析失败的句柄）。
            if (kernelName is null) kernelName = QueryObjectName(local);
            if (kernelName is null || targetKernelName.Length == 0) return;

            if (FileLockService.MatchesTargetPath(targetKernelName, kernelName, isDirectory))
                collector.Add(item.Pid, item.HandleValue, item.TypeIndex, ToDisplayPath(kernelName, deviceMap));
        }, skipKnownHung: true, HardTimeoutMs, progressEvery: 0x3FF, cancellationToken, progress,
            "HandleCleaner_UnlockProgressFormat", "已检查 {0} / {1} 个文件句柄");

        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
        if (result.Error is not null) throw new InvalidOperationException(result.Error.Message, result.Error);

        return new PathHandleScanResult
        {
            Groups = collector.Build(),
            IsDirectoryTarget = isDirectory,
            ScannedHandles = result.Processed,
            SkippedProcesses = result.SkippedProcesses,
            GuardedHandles = result.GuardedHandles,
            Truncated = result.Truncated,
            Elapsed = stopwatch.Elapsed,
        };
    }

    /// <summary>目标路径归一化：能取 final path 就取，否则退回 GetFullPath。</summary>
    private static string NormalizeTargetPath(string path)
    {
        try
        {
            return FileLockService.NormalizeFinalPath(Path.GetFullPath(path));
        }
        catch
        {
            return path;
        }
    }

    // ══════════════════ 深度模式：强制关闭（带复核） ══════════════════

    public static Task<ForceCloseOutcome> ForceCloseAsync(IReadOnlyList<ForceCloseGroup> groups,
        CancellationToken cancellationToken, IProgress<string>? progress = null)
        => Task.Run(() => ForceClose(groups, cancellationToken, progress), cancellationToken);

    /// <summary>
    /// 强制关闭任意句柄（深度模式的核心）。安全措施（即使「更自由」也不放开）：
    /// 1) 系统关键进程硬护栏（IsForceCloseTargetAllowed）；
    /// 2) 进程身份核对（防 PID 复用）；
    /// 3) 关闭前「类型 + 路径」复核：句柄值可能已被回收成别的对象，
    ///    与扫描时的类型/路径对不上就跳过（RecycledSkipped / Unverifiable）。
    /// </summary>
    public static ForceCloseOutcome ForceClose(IReadOnlyList<ForceCloseGroup> groups,
        CancellationToken cancellationToken, IProgress<string>? progress = null)
    {
        TryEnableDebugPrivilege();
        var collector = new ForceCloseCollector();

        // 1) 身份与护栏；收集需要复核的进程集合。
        var accepted = new List<ForceCloseGroup>();
        var pids = new HashSet<int>();
        foreach (var group in groups)
        {
            if (!HandleCleanerPolicy.IsForceCloseTargetAllowed(group.ProcessId, group.Name))
            {
                collector.AddProtected(group.Handles.Count);
                continue;
            }
            if (FileLockService.VerifyProcessIdentity(group.ProcessId, group.Name, group.ImagePath, group.StartTimeUtc)
                != ProcessIdentityCheck.Match)
            {
                collector.AddProcessGone(group.Handles.Count);
                continue;
            }

            accepted.Add(group);
            pids.Add(group.ProcessId);
        }

        // 2) 新快照：拿到每个句柄**当前**的类型索引（句柄回收复核用）。
        var currentTypes = QueryCurrentHandleTypes(pids, cancellationToken);

        var work = new List<ScanItem<ForceCloseHandleRequest>>();
        foreach (var group in accepted)
        {
            foreach (var handle in group.Handles)
            {
                work.Add(new ScanItem<ForceCloseHandleRequest>(group.ProcessId, handle.HandleValue,
                    handle.ExpectedTypeIndex, 0, handle));
            }
        }

        progress?.Report(LocalizationService.L("HandleCleaner_ForceClosePhase", "正在关闭句柄…"));
        var result = RunShard(work, (item, process, local, error) =>
            ForceCloseOne(item, process, local, error, currentTypes, collector),
            skipKnownHung: SkipKnownHungForForceClose, CloseHardTimeoutMs, progressEvery: 0x3F, cancellationToken,
            progress, "HandleCleaner_ForceProgressFormat", "正在关闭句柄 {0} / {1}");

        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
        if (result.Error is not null) throw new InvalidOperationException(result.Error.Message, result.Error);

        return collector.Build(result.Truncated);
    }

    private static void ForceCloseOne(ScanItem<ForceCloseHandleRequest> item, IntPtr process, IntPtr local, int error,
        Dictionary<(int Pid, ulong Handle), ushort> currentTypes, ForceCloseCollector collector)
    {
        if (process == IntPtr.Zero)
        {
            collector.AccessDenied();
            return;
        }
        if (local == IntPtr.Zero)
        {
            if (error == HandleCleanerNative.ErrorInvalidHandle) collector.AlreadyGone();
            else collector.AccessDenied();
            return;
        }

        // 复核：句柄值当前是否仍是同一个对象。
        ushort? currentType = currentTypes.TryGetValue((item.Pid, item.HandleValue), out ushort type) ? type : null;
        string? currentPath = null;
        if (item.Extra.IsFile && currentType == item.Extra.ExpectedTypeIndex &&
            FileLockNative.GetFileType(local) == FileLockNative.FileTypeDisk)
        {
            currentPath = TryResolvePathLight(local);
        }

        var decision = HandleCleanerPolicy.DecideForceClose(item.Extra.ExpectedTypeIndex, currentType,
            item.Extra.ExpectedPath, currentPath);
        switch (decision)
        {
            case ForceCloseDecision.Gone:
                collector.AlreadyGone();
                return;
            case ForceCloseDecision.Recycled:
                collector.Recycled();
                return;
            case ForceCloseDecision.Unverifiable:
                collector.Unverifiable();
                return;
        }

        IntPtr source = new(unchecked((long)item.HandleValue));
        IntPtr current = FileLockNative.GetCurrentProcess();
        if (FileLockNative.DuplicateHandle(process, source, current, out IntPtr closed, 0, false,
                HandleCleanerNative.DuplicateCloseSource | FileLockNative.DuplicateSameAccess))
        {
            FileLockNative.CloseHandle(closed);
            collector.Freed();
            return;
        }

        int closeError = Marshal.GetLastWin32Error();
        if (closeError == HandleCleanerNative.ErrorInvalidHandle) collector.AlreadyGone();
        else collector.AddFailure(item.Pid, item.HandleValue, closeError);
    }

    /// <summary>取一次快照，收集指定进程集合里每个句柄的当前类型索引。</summary>
    private static Dictionary<(int Pid, ulong Handle), ushort> QueryCurrentHandleTypes(HashSet<int> pids,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<(int Pid, ulong Handle), ushort>();
        if (pids.Count == 0) return map;

        IntPtr buffer = QueryHandleTable(out int count);
        try
        {
            int entrySize = Marshal.SizeOf<FileLockNative.SystemHandleTableEntryInfoEx>();
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = Marshal.PtrToStructure<FileLockNative.SystemHandleTableEntryInfoEx>(
                    EntryPointer(buffer, i, entrySize));

                ulong rawPid = entry.UniqueProcessId.ToUInt64();
                if (rawPid > int.MaxValue) continue;
                int pid = (int)rawPid;
                if (!pids.Contains(pid)) continue;

                map[(pid, entry.HandleValue.ToUInt64())] = entry.ObjectTypeIndex;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return map;
    }

    // ══════════════════ 分片扫描引擎 ══════════════════

    private readonly record struct ScanItem<TExtra>(int Pid, ulong HandleValue, ushort TypeIndex, uint GrantedAccess,
        TExtra Extra);

    private sealed class Runner
    {
        public int Start;
        public int End;
        public int Index;
        public volatile bool Done;
        public int Abandoned;

        /// <summary>
        /// 分片当前正在查询的副本句柄：卡住时看门狗负责把它关掉（卡住的线程 finally 永远跑不到）。
        /// 仅用 Interlocked/Volatile 访问；谁取到发布槽谁负责关闭（恰好一次）。
        /// </summary>
        public IntPtr QueryingHandle;
    }

    private sealed class ShardResult
    {
        public int Processed;
        public int SkippedProcesses;
        public int GuardedHandles;
        public bool Truncated;
        public Exception? Error;
    }

    /// <summary>
    /// 「分片线程 + 看门狗」引擎：所有逐句柄的深度操作（判定 / 取名 / 关闭）共用，
    /// 保证单个卡死的句柄不会拖死整轮操作。
    /// 处理器收到的是**已复制到本进程**的句柄（local）；process/local 为 Zero 时按 error 归类。
    /// </summary>
    private sealed class ShardRunner<TExtra>
    {
        public delegate void ItemProcessor(ScanItem<TExtra> item, IntPtr process, IntPtr local, int error);

        private readonly IReadOnlyList<ScanItem<TExtra>> _work;
        private readonly ItemProcessor _processor;
        private readonly bool _skipKnownHung;
        private readonly int _hardTimeoutMs;
        private readonly int _progressEvery;
        private readonly IProgress<string>? _progress;
        private readonly string _progressKey;
        private readonly string _progressFallback;

        private readonly object _resultLock = new();
        private readonly List<Runner> _runners = new();
        private readonly HashSet<int> _skippedPids = new();

        private int _processed;
        private int _pendingRunners;
        private int _exhaustedRunners;
        private int _guardedHandles;
        private int _runnerSeq;
        private Exception? _error;

        public ShardRunner(IReadOnlyList<ScanItem<TExtra>> work, ItemProcessor processor, bool skipKnownHung,
            int hardTimeoutMs, int progressEvery, IProgress<string>? progress, string progressKey,
            string progressFallback)
        {
            _work = work;
            _processor = processor;
            _skipKnownHung = skipKnownHung;
            _hardTimeoutMs = hardTimeoutMs;
            _progressEvery = progressEvery;
            _progress = progress;
            _progressKey = progressKey;
            _progressFallback = progressFallback;
        }

        private int Processed => Volatile.Read(ref _processed);
        private int PendingRunners => Volatile.Read(ref _pendingRunners);
        private int ExhaustedRunners => Volatile.Read(ref _exhaustedRunners);

        private Exception? Error
        {
            get { lock (_resultLock) return _error; }
        }

        public ShardResult Run(CancellationToken cancellationToken)
        {
            int workerCount = Math.Clamp(Environment.ProcessorCount, 2, 4);
            int slice = (_work.Count + workerCount - 1) / workerCount;

            for (int w = 0; w < workerCount; w++)
            {
                int start = w * slice;
                int end = Math.Min(start + slice, _work.Count);
                if (start < end) Launch(start, end, cancellationToken);
            }

            Watchdog(cancellationToken);

            lock (_resultLock)
            {
                return new ShardResult
                {
                    Processed = _processed,
                    SkippedProcesses = _skippedPids.Count,
                    GuardedHandles = _guardedHandles,
                    Truncated = _pendingRunners > _exhaustedRunners,
                    Error = _error,
                };
            }
        }

        private void Launch(int start, int end, CancellationToken cancellationToken)
        {
            var runner = new Runner { Start = start, End = end, Index = start - 1 };
            lock (_runners) _runners.Add(runner);

            Interlocked.Increment(ref _pendingRunners);
            Interlocked.Increment(ref _activeScanRunners);

            try
            {
                var thread = new Thread(() => RunRunner(runner, cancellationToken))
                {
                    IsBackground = true,
                    Name = $"HandleCleanerScan{Interlocked.Increment(ref _runnerSeq)}",
                };
                thread.Start();
            }
            catch
            {
                runner.Done = true;
                Interlocked.Decrement(ref _pendingRunners);
                Interlocked.Decrement(ref _activeScanRunners);
                throw;
            }
        }

        private void RunRunner(Runner runner, CancellationToken cancellationToken)
        {
            try
            {
                EnumerateRange(runner, cancellationToken);
            }
            catch (Exception ex)
            {
                lock (_resultLock) _error ??= ex;
            }
            finally
            {
                // 先置 Done 再减计数：PendingRunners 归零时保证没有任何线程还在处理。
                runner.Done = true;
                Interlocked.Decrement(ref _pendingRunners);
                Interlocked.Decrement(ref _activeScanRunners);
            }
        }

        private void EnumerateRange(Runner runner, CancellationToken cancellationToken)
        {
            IntPtr current = FileLockNative.GetCurrentProcess();
            var processCache = new Dictionary<int, IntPtr>();

            try
            {
                int total = _work.Count;
                int progressEvery = Math.Max(1, _progressEvery);
                for (int i = runner.Start; i < runner.End; i++)
                {
                    Volatile.Write(ref runner.Index, i);
                    Interlocked.Increment(ref _processed);

                    if (i % progressEvery == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _progress?.Report(string.Format(
                            LocalizationService.L(_progressKey, _progressFallback), Processed, total));
                    }

                    ProcessItem(runner, _work[i], current, processCache);
                }
            }
            finally
            {
                foreach (var handle in processCache.Values)
                {
                    if (handle != IntPtr.Zero) FileLockNative.CloseHandle(handle);
                }
            }
        }

        private void ProcessItem(Runner runner, ScanItem<TExtra> item, IntPtr current,
            Dictionary<int, IntPtr> processCache)
        {
            if (_skipKnownHung && KnownHungHandles.ContainsKey((item.Pid, item.HandleValue)))
            {
                Interlocked.Increment(ref _guardedHandles);
                return;
            }

            if (!processCache.TryGetValue(item.Pid, out IntPtr process))
            {
                process = FileLockNative.OpenProcess((int)FileLockNative.ProcessDupHandle, false, item.Pid);
                processCache[item.Pid] = process;   // 失败也缓存（IntPtr.Zero）：同一进程不重复重试
            }
            if (process == IntPtr.Zero)
            {
                lock (_resultLock) _skippedPids.Add(item.Pid);
                _processor(item, IntPtr.Zero, IntPtr.Zero, Marshal.GetLastWin32Error());
                return;
            }

            IntPtr source = new(unchecked((long)item.HandleValue));
            if (!FileLockNative.DuplicateHandle(process, source, current, out IntPtr local, 0, false,
                    FileLockNative.DuplicateSameAccess))
            {
                _processor(item, process, IntPtr.Zero, Marshal.GetLastWin32Error());
                return;
            }

            try
            {
                Volatile.Write(ref runner.QueryingHandle, local);
                _processor(item, process, local, 0);
            }
            finally
            {
                // 谁取到发布槽谁负责关闭：看门狗已经替我们关过（返回 0）就不重复关。
                if (Interlocked.Exchange(ref runner.QueryingHandle, IntPtr.Zero) != IntPtr.Zero)
                    FileLockNative.CloseHandle(local);
            }
        }

        /// <summary>
        /// 看门狗：进度停滞（分片卡在原生调用里）时登记卡死句柄、回收副本句柄并从卡住位置续跑。
        /// </summary>
        private void Watchdog(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            int lastProcessed = -1;
            long lastChangeMs = 0;
            int recoveries = 0;

            while (PendingRunners > 0 && stopwatch.ElapsedMilliseconds < _hardTimeoutMs)
            {
                Thread.Sleep(WatchdogTickMs);

                // 取消优先：工作线程可能正卡在原生调用里，看门狗负责让调用方及时返回。
                if (cancellationToken.IsCancellationRequested) break;

                int processed = Processed;
                if (processed != lastProcessed)
                {
                    lastProcessed = processed;
                    lastChangeMs = stopwatch.ElapsedMilliseconds;
                    continue;
                }

                long stalledMs = stopwatch.ElapsedMilliseconds - lastChangeMs;

                // 只剩一个分片没动静时基本可以断定它卡住了，不必等满 StallTimeout。
                long limit = PendingRunners <= 1 ? IdleAbandonMs : StallTimeoutMs;
                if (stalledMs < limit) continue;

                if (recoveries < MaxStallRecoveries)
                {
                    int resumed = RecoverStalled(cancellationToken);
                    if (resumed > 0)
                    {
                        recoveries += resumed;
                        lastChangeMs = stopwatch.ElapsedMilliseconds;
                        continue;
                    }
                }

                break;
            }
        }

        /// <summary>
        /// 卡住恢复：把每个卡住分片正处理的句柄登记为「已知卡死」（后续扫描直接跳过），
        /// 回收它发布的副本句柄，并从卡住位置之后续跑剩余区间。
        /// </summary>
        private int RecoverStalled(CancellationToken cancellationToken)
        {
            List<Runner> stalled;
            lock (_runners) stalled = _runners.Where(r => !r.Done).ToList();

            int resumed = 0;
            foreach (var runner in stalled)
            {
                int stuckAt = Volatile.Read(ref runner.Index);
                RememberHung(stuckAt);

                IntPtr stuckHandle = Interlocked.Exchange(ref runner.QueryingHandle, IntPtr.Zero);
                if (stuckHandle != IntPtr.Zero)
                {
                    FileLockNative.CloseHandle(stuckHandle);
                    Debug.WriteLine($"[HandleCleaner] 已回收卡死分片持有的副本句柄 0x{stuckHandle.ToInt64():X}");
                }

                bool alreadyAbandoned = Interlocked.CompareExchange(ref runner.Abandoned, 1, 0) == 1;
                if (!FileLockService.TryPlanResume(runner.Start, runner.End, stuckAt, alreadyAbandoned, out int resumeFrom))
                {
                    // 没有剩余区间可扫：卡在片尾（本轮新放弃，计数）或已被续跑覆盖（不重复计数）。
                    Interlocked.Increment(ref _exhaustedRunners);
                    if (!alreadyAbandoned) Interlocked.Increment(ref _guardedHandles);
                    continue;
                }

                Interlocked.Increment(ref _guardedHandles);
                Launch(resumeFrom, runner.End, cancellationToken);
                resumed++;
            }

            return resumed;
        }

        private void RememberHung(int index)
        {
            if (index < 0 || index >= _work.Count) return;
            if (KnownHungHandles.Count >= MaxKnownHungHandles) return;

            var item = _work[index];
            if (KnownHungHandles.TryAdd((item.Pid, item.HandleValue), 0))
            {
                Debug.WriteLine($"[HandleCleaner] 登记卡死句柄 pid={item.Pid} handle=0x{item.HandleValue:X}：后续扫描直接跳过");
            }
        }
    }

    private static ShardResult RunShard<TExtra>(List<ScanItem<TExtra>> work,
        ShardRunner<TExtra>.ItemProcessor processor, bool skipKnownHung, int hardTimeoutMs, int progressEvery,
        CancellationToken cancellationToken, IProgress<string>? progress, string progressKey, string progressFallback)
    {
        if (work.Count == 0) return new ShardResult();

        var runner = new ShardRunner<TExtra>(work, processor, skipKnownHung, hardTimeoutMs, progressEvery,
            progress, progressKey, progressFallback);
        return runner.Run(cancellationToken);
    }

    // ══════════════════ 判定（分层探针） ══════════════════

    private static HandleVerdict ClassifyHandle(IntPtr handle, out string? displayPath)
    {
        displayPath = null;

        var info = default(HandleCleanerNative.FileStandardInfoData);
        if (HandleCleanerNative.GetFileInformationByHandleEx(handle, HandleCleanerNative.FileStandardInfo, ref info,
                (uint)Marshal.SizeOf<HandleCleanerNative.FileStandardInfoData>()))
        {
            if (!info.DeletePending) return HandleVerdict.Normal;
            displayPath = TryResolvePathLight(handle);
            return HandleVerdict.Deleted;
        }

        string? path = TryResolvePathFull(handle);
        if (path is null) return HandleVerdict.Unknown;

        displayPath = path;
        bool? missing = HandleCleanerPolicy.IsDeletedPseudoPath(path) ? true : FileOrDirectoryMissing(path);
        return HandleCleanerPolicy.DecideFromPath(path, missing);
    }

    /// <summary>轻量解析（不做 NtQueryObject）：DOS final path → NT 设备路径 + 盘符映射。</summary>
    private static string? TryResolvePathLight(IntPtr handle)
    {
        string? dos = QueryFinalPath(handle, FileLockNative.VolumeNameDos);
        if (dos is not null) return FileLockService.NormalizeFinalPath(dos);

        string? nt = QueryFinalPath(handle, HandleCleanerNative.VolumeNameNt);
        return nt is null ? null : ToDisplayPath(nt);
    }

    /// <summary>完整解析（含 NtQueryObject 兜底）：只在第一层判定失败时使用。</summary>
    private static string? TryResolvePathFull(IntPtr handle)
    {
        string? light = TryResolvePathLight(handle);
        if (light is not null) return light;

        string? name = QueryObjectName(handle);
        return name is null ? null : ToDisplayPath(name);
    }

    private static string? QueryFinalPath(IntPtr handle, uint flags)
    {
        var buffer = new StringBuilder(1024);
        uint length = FileLockNative.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, flags);
        if (length == 0) return null;

        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder((int)length + 1);
            length = FileLockNative.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, flags);
            if (length == 0 || length >= buffer.Capacity) return null;
        }

        return buffer.ToString();
    }

    private static string? QueryObjectName(IntPtr handle)
    {
        int size = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            int status = FileLockNative.NtQueryObject(handle, FileLockNative.ObjectNameInformation, buffer, size,
                out int needed);
            if (status == FileLockNative.StatusInfoLengthMismatch && needed > 0)
            {
                Marshal.FreeHGlobal(buffer);
                size = needed;
                buffer = Marshal.AllocHGlobal(size);
                status = FileLockNative.NtQueryObject(handle, FileLockNative.ObjectNameInformation, buffer, size, out _);
            }

            if (status < 0) return null;

            var unicode = Marshal.PtrToStructure<FileLockNative.UnicodeString>(buffer);
            if (unicode.Buffer == IntPtr.Zero || unicode.Length == 0) return null;
            return Marshal.PtrToStringUni(unicode.Buffer, unicode.Length / 2);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HandleCleaner] NtQueryObject 失败: {ex.Message}");
            return null;
        }
        finally
        {
            try { Marshal.FreeHGlobal(buffer); } catch { /* 已释放 */ }
        }
    }

    /// <summary>
    /// 存在性探测：用 0 访问权限打开（不干扰持有者），只有「文件/路径确实不存在」
    /// 才回答 true；权限不足等一律回答 null（不可判定，保守处理）。
    /// </summary>
    private static bool? FileOrDirectoryMissing(string path)
    {
        IntPtr handle = FileLockNative.CreateFileW(path, 0, FileLockNative.ShareAll, IntPtr.Zero,
            FileLockNative.OpenExisting, FileLockNative.FileFlagBackupSemantics, IntPtr.Zero);
        if (handle != FileLockNative.InvalidHandleValue)
        {
            FileLockNative.CloseHandle(handle);
            return false;
        }

        int error = Marshal.GetLastWin32Error();
        return error switch
        {
            HandleCleanerNative.ErrorFileNotFound => true,
            HandleCleanerNative.ErrorPathNotFound => true,
            HandleCleanerNative.ErrorInvalidName => true,
            _ => null,
        };
    }

    // ---------- 设备路径 → 盘符映射 ----------

    private static Dictionary<string, string> BuildDeviceMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                string letter = drive.Name.TrimEnd('\\');
                var target = new StringBuilder(1024);
                if (FileLockNative.QueryDosDeviceW(letter, target, target.Capacity) == 0) continue;

                string device = target.ToString();
                int end = device.IndexOf('\0');
                if (end >= 0) device = device[..end];
                if (device.Length > 0) map[device] = letter;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HandleCleaner] 盘符映射失败（显示将退回内核名）: {ex.Message}");
        }
        return map;
    }

    private static string ToDisplayPath(string kernelName) => ToDisplayPath(kernelName, BuildDeviceMap());

    private static string ToDisplayPath(string kernelName, IReadOnlyDictionary<string, string> deviceMap)
    {
        string bestDevice = "";
        string bestDrive = "";

        foreach (var (device, drive) in deviceMap)
        {
            if (!kernelName.StartsWith(device, StringComparison.OrdinalIgnoreCase)) continue;
            if (device.Length <= bestDevice.Length) continue;
            if (kernelName.Length > device.Length && kernelName[device.Length] != '\\') continue;

            bestDevice = device;
            bestDrive = drive;
        }

        return bestDevice.Length == 0 ? kernelName : bestDrive + kernelName[bestDevice.Length..];
    }

    // ══════════════════ 诊断报告 ══════════════════

    public static string BuildReport(HandleCleanerOverview? overview, CandidateScanResult? scan, bool isAdmin)
    {
        var text = new StringBuilder();
        text.AppendLine(LocalizationService.L("HandleCleaner_ReportHeader", "句柄清理诊断报告"));
        text.AppendLine(new string('-', 34));

        if (overview is not null)
        {
            text.AppendLine(string.Format(
                LocalizationService.L("HandleCleaner_ReportSystemFormat", "系统句柄总数：{0}　|　进程数：{1}　|　疑似泄漏进程：{2}"),
                overview.TotalHandles, overview.ProcessCount, overview.SuspectedLeakCount));
        }

        text.AppendLine(string.Format(
            LocalizationService.L("HandleCleaner_ReportAdminFormat", "运行身份：{0}"),
            isAdmin
                ? LocalizationService.L("HandleCleaner_ReportAdminYes", "管理员")
                : LocalizationService.L("HandleCleaner_ReportAdminNo", "普通用户")));

        if (scan is null)
        {
            text.AppendLine(LocalizationService.L("HandleCleaner_ReportNoScan", "本次会话还没有执行失效句柄扫描。"));
        }
        else
        {
            text.AppendLine(string.Format(
                LocalizationService.L("HandleCleaner_ReportScanFormat",
                    "失效句柄扫描：检查文件句柄 {0} 个，发现失效 {1} 个（涉及 {2} 个进程），无法判定 {3} 个，跳过进程 {4} 个"),
                scan.ScannedFileHandles, scan.TotalCandidates, scan.AffectedProcessCount, scan.TotalUnknown,
                scan.SkippedProcesses));

            if (scan.Truncated) text.AppendLine(LocalizationService.L("HandleCleaner_TruncatedHint", "扫描被截断，结果可能不完整"));

            var withCandidates = scan.Groups.Where(g => g.Handles.Count > 0)
                .OrderByDescending(g => g.Handles.Count).ThenBy(g => g.ProcessId)
                .Take(20).ToList();
            if (withCandidates.Count > 0)
            {
                text.AppendLine(LocalizationService.L("HandleCleaner_ReportCandidates", "失效句柄分布："));
                foreach (var group in withCandidates)
                {
                    text.AppendLine(string.Format(
                        LocalizationService.L("HandleCleaner_ReportCandidateRowFormat", "· {0} (PID {1}) — 失效 {2} 个"),
                        group.Name, group.ProcessId, group.Handles.Count));
                }
            }
        }

        if (overview is not null && overview.Processes.Count > 0)
        {
            text.AppendLine(LocalizationService.L("HandleCleaner_ReportTopProcesses", "句柄占用 TOP 20："));
            int rank = 1;
            foreach (var process in overview.Processes.Take(20))
            {
                text.AppendLine(string.Format(
                    LocalizationService.L("HandleCleaner_ReportRowFormat", "{0}. {1} (PID {2}) — 句柄 {3}（文件 {4}）"),
                    rank++, process.Name, process.ProcessId, process.TotalHandles, process.FileHandles));
            }
        }

        text.AppendLine(string.Format(
            LocalizationService.L("HandleCleaner_ReportGeneratedFormat", "生成时间：{0}"),
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

        return text.ToString();
    }

    // ══════════════════ SeDebugPrivilege ══════════════════

    private static int _debugPrivilegeState;   // 0 = 未尝试，1 = 已启用，2 = 不可用

    /// <summary>启用 SeDebugPrivilege（打开 SYSTEM 进程需要；失败不影响用户进程的扫描与清理）。</summary>
    internal static bool TryEnableDebugPrivilege()
    {
        if (Volatile.Read(ref _debugPrivilegeState) != 0)
            return Volatile.Read(ref _debugPrivilegeState) == 1;

        bool enabled = false;
        IntPtr token = IntPtr.Zero;
        try
        {
            if (HandleCleanerNative.OpenProcessToken(FileLockNative.GetCurrentProcess(),
                    HandleCleanerNative.TokenAdjustPrivileges | HandleCleanerNative.TokenQuery, out token))
            {
                if (HandleCleanerNative.LookupPrivilegeValueW(null, "SeDebugPrivilege", out var luid))
                {
                    var privileges = new HandleCleanerNative.TokenPrivileges
                    {
                        PrivilegeCount = 1,
                        Luid = luid,
                        Attributes = HandleCleanerNative.SePrivilegeEnabled,
                    };

                    if (HandleCleanerNative.AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                    {
                        // AdjustTokenPrivileges 即使部分失败也返回 true，必须检查 GetLastError。
                        enabled = Marshal.GetLastWin32Error() != HandleCleanerNative.ErrorNotAllAssigned;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HandleCleaner] 启用 SeDebugPrivilege 失败: {ex.Message}");
        }
        finally
        {
            if (token != IntPtr.Zero) FileLockNative.CloseHandle(token);
        }

        Volatile.Write(ref _debugPrivilegeState, enabled ? 1 : 2);
        if (!enabled) Debug.WriteLine("[HandleCleaner] SeDebugPrivilege 不可用：SYSTEM 进程将被跳过");
        return enabled;
    }

    // ══════════════════ 句柄表快照 ══════════════════

    /// <summary>
    /// 取一份句柄表快照（非托管缓冲，调用方负责 FreeHGlobal）。
    /// 头 16 字节 { NumberOfHandles, Reserved }，其后是定长条目数组。
    /// </summary>
    private static IntPtr QueryHandleTable(out int handleCount)
    {
        int size = FileLockNative.InitialHandleBufferSize;

        while (true)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            int status = FileLockNative.NtQuerySystemInformation(FileLockNative.SystemExtendedHandleInformation, buffer,
                size, out int needed);

            if (status == 0)
            {
                var header = Marshal.PtrToStructure<FileLockNative.SystemHandleInformationExHeader>(buffer);
                handleCount = unchecked((int)header.NumberOfHandles.ToUInt64());
                return buffer;
            }

            Marshal.FreeHGlobal(buffer);

            if (status != FileLockNative.StatusInfoLengthMismatch)
                throw new InvalidOperationException($"NtQuerySystemInformation 失败：0x{status:X8}");

            if (needed <= size) needed = size * 2;
            if (needed > FileLockNative.MaxHandleBufferSize)
                throw new InvalidOperationException("句柄表超出上限，放弃扫描");

            size = needed;
        }
    }

    private static IntPtr EntryPointer(IntPtr buffer, int index, int entrySize)
        => buffer + Marshal.SizeOf<FileLockNative.SystemHandleInformationExHeader>() + index * entrySize;

    /// <summary>
    /// 在单次快照里把自建样本句柄的 ObjectTypeIndex 找出来（只看我们自己进程的条目 ——
    /// 外部进程条目的 Object 指针会读成 0，但 (pid, handleValue) 永远可靠）。
    /// </summary>
    private static Dictionary<string, ushort> ResolveSampleTypeIndexes(TypeSampleSet samples, IntPtr buffer, int count)
    {
        var map = new Dictionary<string, ushort>(StringComparer.Ordinal);
        if (samples.Items.Count == 0) return map;

        var handleToName = new Dictionary<ulong, string>();
        foreach (var (handle, name) in samples.Items)
            handleToName[unchecked((ulong)handle.ToInt64())] = name;

        int entrySize = Marshal.SizeOf<FileLockNative.SystemHandleTableEntryInfoEx>();
        ulong selfPid = (ulong)Environment.ProcessId;

        for (int i = 0; i < count && map.Count < handleToName.Count; i++)
        {
            var entry = Marshal.PtrToStructure<FileLockNative.SystemHandleTableEntryInfoEx>(
                EntryPointer(buffer, i, entrySize));

            if (entry.UniqueProcessId.ToUInt64() != selfPid) continue;
            if (handleToName.TryGetValue(entry.HandleValue.ToUInt64(), out string? name))
                map[name] = entry.ObjectTypeIndex;
        }

        return map;
    }

    // ══════════════════ 结果收集 ══════════════════

    private sealed class ProcessAccumulator
    {
        public int Total;
        public int File;
        public readonly Dictionary<ushort, int> Types = new();
    }

    private sealed class CandidateCollector
    {
        private readonly object _lock = new();
        private readonly Dictionary<int, CandidateProcessGroup> _groups = new();
        private readonly ProcessInfoCache _processInfo;

        public CandidateCollector(ProcessInfoCache processInfo) => _processInfo = processInfo;

        public void AddCandidate(int pid, ulong handleValue, string displayPath)
        {
            lock (_lock)
            {
                var group = GetOrCreateLocked(pid);
                group.Handles.Add(new CandidateHandle { HandleValue = handleValue, DisplayPath = displayPath });
            }
        }

        public void AddUnknown(int pid)
        {
            lock (_lock) GetOrCreateLocked(pid).UnknownCount++;
        }

        private CandidateProcessGroup GetOrCreateLocked(int pid)
        {
            if (_groups.TryGetValue(pid, out var group)) return group;

            var (name, path, startUtc) = _processInfo.Get(pid);
            group = new CandidateProcessGroup
            {
                ProcessId = pid,
                Name = name,
                ImagePath = path,
                StartTimeUtc = startUtc,
            };
            _groups[pid] = group;
            return group;
        }

        public IReadOnlyList<CandidateProcessGroup> Build()
        {
            lock (_lock)
            {
                return _groups.Values
                    .OrderByDescending(g => g.Handles.Count)
                    .ThenBy(g => g.ProcessId)
                    .ToList();
            }
        }
    }

    private sealed class PathMatchCollector
    {
        private readonly object _lock = new();
        private readonly Dictionary<int, PathHandleGroup> _groups = new();
        private readonly ProcessInfoCache _processInfo;

        public PathMatchCollector(ProcessInfoCache processInfo) => _processInfo = processInfo;

        public void Add(int pid, ulong handleValue, ushort typeIndex, string displayPath)
        {
            lock (_lock)
            {
                if (!_groups.TryGetValue(pid, out var group))
                {
                    var (name, path, startUtc) = _processInfo.Get(pid);
                    group = new PathHandleGroup
                    {
                        ProcessId = pid,
                        Name = name,
                        ImagePath = path,
                        StartTimeUtc = startUtc,
                    };
                    _groups[pid] = group;
                }

                group.Handles.Add(new PathHandleItem
                {
                    HandleValue = handleValue,
                    TypeIndex = typeIndex,
                    DisplayPath = displayPath,
                });
            }
        }

        public IReadOnlyList<PathHandleGroup> Build()
        {
            lock (_lock)
            {
                return _groups.Values
                    .OrderByDescending(g => g.Handles.Count)
                    .ThenBy(g => g.ProcessId)
                    .ToList();
            }
        }
    }

    private sealed class CleanCollector
    {
        private readonly object _lock = new();
        private readonly List<string> _failureSamples = new();

        private int _freed;
        private int _alreadyGone;
        private int _accessDenied;
        private int _processGone;
        private int _otherFailures;
        private int _skippedType;

        public void Freed() => Interlocked.Increment(ref _freed);
        public void AlreadyGone() => Interlocked.Increment(ref _alreadyGone);
        public void AccessDenied() => Interlocked.Increment(ref _accessDenied);
        public void SkippedType() => Interlocked.Increment(ref _skippedType);

        public void AddAccessDenied(int count) => Interlocked.Add(ref _accessDenied, count);
        public void AddProcessGone(int count) => Interlocked.Add(ref _processGone, count);

        public void AddFailure(int pid, string processName, ulong handleValue, int error)
        {
            Interlocked.Increment(ref _otherFailures);
            lock (_lock)
            {
                if (_failureSamples.Count < MaxFailureSamples)
                    _failureSamples.Add($"{processName} (PID {pid}) 0x{handleValue:X} → 错误 {error}");
            }
        }

        public CleanOutcome Build(bool truncated) => new()
        {
            Freed = _freed,
            AlreadyGone = _alreadyGone,
            AccessDenied = _accessDenied,
            ProcessGone = _processGone,
            OtherFailures = _otherFailures,
            SkippedType = _skippedType,
            TimedOut = truncated,
            FailureSamples = _failureSamples.ToArray(),
        };
    }

    private sealed class ForceCloseCollector
    {
        private readonly object _lock = new();
        private readonly List<string> _failureSamples = new();

        private int _freed;
        private int _alreadyGone;
        private int _recycled;
        private int _unverifiable;
        private int _accessDenied;
        private int _processGone;
        private int _protected;
        private int _otherFailures;

        public void Freed() => Interlocked.Increment(ref _freed);
        public void AlreadyGone() => Interlocked.Increment(ref _alreadyGone);
        public void Recycled() => Interlocked.Increment(ref _recycled);
        public void Unverifiable() => Interlocked.Increment(ref _unverifiable);
        public void AccessDenied() => Interlocked.Increment(ref _accessDenied);
        public void AddProtected(int count) => Interlocked.Add(ref _protected, count);
        public void AddProcessGone(int count) => Interlocked.Add(ref _processGone, count);

        public void AddFailure(int pid, ulong handleValue, int error)
        {
            Interlocked.Increment(ref _otherFailures);
            lock (_lock)
            {
                if (_failureSamples.Count < MaxFailureSamples)
                    _failureSamples.Add($"PID {pid} 0x{handleValue:X} → 错误 {error}");
            }
        }

        public ForceCloseOutcome Build(bool truncated) => new()
        {
            Freed = _freed,
            AlreadyGone = _alreadyGone,
            RecycledSkipped = _recycled,
            Unverifiable = _unverifiable,
            AccessDenied = _accessDenied,
            ProcessGone = _processGone,
            ProtectedSkipped = _protected,
            OtherFailures = _otherFailures,
            Truncated = truncated,
            FailureSamples = _failureSamples.ToArray(),
        };
    }

    // ══════════════════ 进程信息 ══════════════════

    /// <summary>进程名 / 映像路径 / 启动时间，按 PID 缓存（与 FileLockService 同款信息源）。</summary>
    private sealed class ProcessInfoCache
    {
        private readonly object _lock = new();
        private readonly Dictionary<int, (string Name, string Path, DateTime? StartUtc)> _cache = new();

        public (string Name, string Path, DateTime? StartUtc) Get(int pid)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(pid, out var cached)) return cached;

                (string Name, string Path, DateTime? StartUtc) info;
                if (pid <= 4)
                {
                    info = ("System", "", null);
                }
                else
                {
                    string name;
                    DateTime? startUtc = null;
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        name = process.ProcessName;
                        try { startUtc = process.StartTime.ToUniversalTime(); }
                        catch { /* 受保护进程读不到启动时间 */ }
                    }
                    catch
                    {
                        name = FileLockService.ProcessNameFallbackPrefix + pid;
                    }
                    info = (name, FileLockService.QueryProcessPath(pid), startUtc);
                }

                _cache[pid] = info;
                return info;
            }
        }
    }

    // ══════════════════ 自建样本句柄 ══════════════════

    /// <summary>
    /// 建立「内核类型名 → ObjectTypeIndex」映射所需的样本句柄集合。
    /// ObjectTypeIndex 每次开机由内核动态分配（不同机器/不同启动可能不同），
    /// 所以必须运行时自建对象再回查，不能写死数字。
    /// 创建失败的样本自动跳过（最坏情况退化为「其他」显示）。
    /// </summary>
    private sealed class TypeSampleSet : IDisposable
    {
        public readonly List<(IntPtr Handle, string Name)> Items = new();

        public static TypeSampleSet Create()
        {
            var set = new TypeSampleSet();
            set.TryAdd("File", OpenFileSample);
            set.TryAdd("Event", () => HandleCleanerNative.CreateEventW(IntPtr.Zero, false, false, null));
            set.TryAdd("Mutant", () => HandleCleanerNative.CreateMutexW(IntPtr.Zero, false, null));
            set.TryAdd("Semaphore", () => HandleCleanerNative.CreateSemaphoreW(IntPtr.Zero, 1, 1, null));
            set.TryAdd("Section", () => HandleCleanerNative.CreateFileMappingW(FileLockNative.InvalidHandleValue,
                IntPtr.Zero, HandleCleanerNative.PageReadWrite, 0, 4096, null));
            set.TryAdd("Timer", () => HandleCleanerNative.CreateWaitableTimerW(IntPtr.Zero, false, null));
            set.TryAdd("IoCompletion", () => HandleCleanerNative.CreateIoCompletionPort(FileLockNative.InvalidHandleValue,
                IntPtr.Zero, UIntPtr.Zero, 0));
            set.TryAdd("Process", () => FileLockNative.OpenProcess((int)FileLockNative.ProcessQueryLimitedInformation,
                false, Environment.ProcessId));
            set.TryAdd("Thread", () => HandleCleanerNative.OpenThread(HandleCleanerNative.ThreadQueryLimitedInformation,
                false, HandleCleanerNative.GetCurrentThreadId()));
            set.TryAdd("Token", () => HandleCleanerNative.OpenProcessToken(FileLockNative.GetCurrentProcess(),
                HandleCleanerNative.TokenQuery, out IntPtr token) ? token : IntPtr.Zero);
            set.TryAdd("Key", OpenKeySample);
            return set;
        }

        private static IntPtr OpenFileSample()
        {
            foreach (string path in EnumerateFileSamplePaths())
            {
                IntPtr handle = FileLockNative.CreateFileW(path, 0, FileLockNative.ShareAll, IntPtr.Zero,
                    FileLockNative.OpenExisting, 0, IntPtr.Zero);
                if (handle != FileLockNative.InvalidHandleValue) return handle;
            }
            return IntPtr.Zero;
        }

        private static IEnumerable<string> EnumerateFileSamplePaths()
        {
            string? main = null;
            try { main = Process.GetCurrentProcess().MainModule?.FileName; }
            catch { /* 忽略 */ }
            if (!string.IsNullOrEmpty(main)) yield return main;

            string local = Path.Combine(AppContext.BaseDirectory, "TubaWinUi3.exe");
            if (File.Exists(local) && !string.Equals(local, main, StringComparison.OrdinalIgnoreCase))
                yield return local;

            string kernel32 = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
            if (File.Exists(kernel32)) yield return kernel32;
        }

        private static IntPtr OpenKeySample()
        {
            int result = HandleCleanerNative.RegOpenKeyExW(HandleCleanerNative.HKeyLocalMachine, "SOFTWARE", 0,
                HandleCleanerNative.KeyRead, out IntPtr key);
            return result == 0 ? key : IntPtr.Zero;
        }

        private void TryAdd(string name, Func<IntPtr> create)
        {
            try
            {
                IntPtr handle = create();
                if (handle != IntPtr.Zero && handle != FileLockNative.InvalidHandleValue)
                    Items.Add((handle, name));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HandleCleaner] 创建 {name} 样本句柄失败: {ex.Message}");
            }
        }

        public void Dispose()
        {
            foreach (var (handle, _) in Items) FileLockNative.CloseHandle(handle);
            Items.Clear();
        }
    }
}
