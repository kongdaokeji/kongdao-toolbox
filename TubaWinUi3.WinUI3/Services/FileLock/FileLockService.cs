using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TubaWinUi3.Services.FileLock;

/// <summary>
/// 一个占用目标路径的进程，以及它持有的相关路径（扫描目录时是具体子文件）。
/// </summary>
public sealed class FileLockEntry
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string ProcessPath { get; init; } = "";

    /// <summary>
    /// 扫描时该进程的启动时间（UTC）。结束进程前用它核对 PID 是否已被复用；读不到时为 null。
    /// </summary>
    public DateTime? StartTimeUtc { get; init; }

    /// <summary>该进程持有的、与目标匹配的路径（已去重，保持发现顺序）。</summary>
    public IReadOnlyList<string> LockedPaths { get; init; } = Array.Empty<string>();
}

/// <summary>扫描失败原因。页面据此给出可本地化的提示。</summary>
public enum FileLockScanError
{
    None,
    EmptyPath,
    NotFound,
    ResolveFailed,
    Failed
}

/// <summary>结束进程前的身份核对结果（页面据此决定是否放行 Kill）。</summary>
internal enum ProcessIdentityCheck
{
    /// <summary>仍是扫描时的那一个进程，可以结束。</summary>
    Match,

    /// <summary>进程已退出（或已打不开）。</summary>
    Exited,

    /// <summary>进程还在但身份对不上：PID 已被复用；或信息不足，无法确认。</summary>
    Mismatch
}

/// <summary>
/// 扫描失败的具体原因（显示层据此翻文案；数值参数见 <see cref="FileLockScanResult.FailureCode"/>）。
/// 服务层不再产用户可见的散文——那会把中英混在一句话里。
/// </summary>
public enum FileLockScanFailure
{
    /// <summary>没有失败。</summary>
    None,

    /// <summary>CreateFileW 打不开目标；FailureCode = Win32 错误码。</summary>
    CreateFileFailed,

    /// <summary>目标不是磁盘上的文件或目录。</summary>
    NotDiskFile,

    /// <summary>拿不到目标的内核设备名。</summary>
    DeviceNameUnavailable,

    /// <summary>定位不到 File 对象类型索引。</summary>
    ObjectTypeIndexUnavailable,

    /// <summary>系统句柄表超出上限，放弃扫描；FailureCode = 需要的缓冲字节数。</summary>
    HandleTableTooLarge,

    /// <summary>NtQuerySystemInformation 失败；FailureCode = NTSTATUS。</summary>
    NtQuerySystemInformationFailed,

    /// <summary>未预期的异常。</summary>
    Unexpected
}

/// <summary>
/// 扫描结果。<see cref="SkippedProcesses"/> / <see cref="Truncated"/> 必须如实呈现给用户，
/// 不能静默吞掉——否则「没扫到」会被误解成「没人占用」。
/// </summary>
public sealed class FileLockScanResult
{
    public IReadOnlyList<FileLockEntry> Entries { get; init; } = Array.Empty<FileLockEntry>();

    /// <summary>实际检查过的句柄数（已按 ObjectTypeIndex 过滤后的外层计数）。</summary>
    public int ScannedHandles { get; init; }

    /// <summary>因权限等原因没能查询的进程数（去重后的 PID 个数）。</summary>
    public int SkippedProcesses { get; init; }

    /// <summary>枚举中途卡住、被看门狗放弃续跑的那几个句柄数（结果可能漏了它们）。</summary>
    public int GuardedHandles { get; init; }

    /// <summary>枚举中途卡住/超时被截断，结果可能不完整。</summary>
    public bool Truncated { get; init; }

    public FileLockScanError Error { get; init; }

    /// <summary>失败的具体原因（Error 为 ResolveFailed / Failed 时有效）。文案由显示层翻译。</summary>
    public FileLockScanFailure Failure { get; init; }

    /// <summary>Failure 的数值参数（Win32 错误码 / NTSTATUS / 需要的缓冲字节数）；无参数为 0。</summary>
    public int FailureCode { get; init; }

    /// <summary>诊断用详情（中文散文，仅供日志与测试）；界面一律走 FileLockErrorText.Describe。</summary>
    public string? ErrorDetail { get; init; }

    public bool Succeeded => Error == FileLockScanError.None;

    public int TotalLockedPaths => Entries.Sum(e => e.LockedPaths.Count);
}

/// <summary>
/// 「文件占用查看」（对标 PowerToys File Locksmith）。
/// 实现要点与取舍见各方法注释；总体策略是**保守**：
/// 宁可少报（GuardedHandles / Truncated 如实计数）也不调用有阻塞风险的查询。
/// 注意「保守」的边界：只对**确实有阻塞风险**的对象退让（管道/设备交给 GetFileType 过滤），
/// 不按 GrantedAccess 整类跳过——那样会把以读写方式打开的文件也一起漏掉。
/// </summary>
public static class FileLockService
{
    // ---------- 卡死防护参数（保守版：不调 TerminateThread） ----------
    private const int WatchdogTickMs = 200;

    /// <summary>连续这么久没有推进就认为卡住，放弃等待并快照已完成结果。</summary>
    private const int StallTimeoutMs = 8000;

    /// <summary>其他分片都已收尾时，给最后一片的宽限期（超过即判定卡住）。</summary>
    private const int IdleAbandonMs = 2000;

    /// <summary>
    /// 整轮扫描最多容忍几个卡死的句柄——卡住的会被跳过并从下一个位置续跑。
    /// 不再让它当「能否扫完」的瓶颈：实测本机会长期存在卡死句柄（驱动持有的读句柄），
    /// 而且每跳过一个就会泄漏一个副本句柄、下一次扫描里它又变成新的卡死项（家族会涨）。
    /// 预算给足，让扫描尽量扫完；真正兜底的是 <see cref="HardTimeoutMs"/>（整轮 60 秒硬上限），
    /// 还有「卡在片尾、没有剩余区间」与「真的放弃」的区分（见 RunWithWatchdog 的收尾判定）。
    /// </summary>
    private const int MaxStallRecoveries = 16;

    /// <summary>整体硬超时。</summary>
    private const int HardTimeoutMs = 60_000;

    /// <summary>
    /// 进程名读不到时的兜底显示（"PID:1234"）。结束前的身份核对会**跳过**带这个前缀的名字——
    /// 否则拿兜底字符串去和真实进程名比较，会把合法目标误判成「PID 已被复用」。
    /// </summary>
    internal const string ProcessNameFallbackPrefix = "PID:";

    private static int _liveSnapshotBuffers;

    /// <summary>仍占着未释放的句柄表快照数。测试用：扫描（含取消）结束后不应增长。</summary>
    internal static int LiveSnapshotBuffers => Volatile.Read(ref _liveSnapshotBuffers);

    private static int _activeScanRunners;

    /// <summary>仍在跑的分片线程数。测试用：等它归零后再断言快照已释放。</summary>
    internal static int ActiveScanRunners => Volatile.Read(ref _activeScanRunners);

    /// <summary>测试注入点：每处理一个句柄回调一次（生产路径为 null，只多一次判空）。</summary>
    internal static Action<int>? TestPerHandleHook;

    /// <summary>
    /// 句柄表快照的缓冲上限（默认 64MB，约 160 万句柄）。可写是给测试用：
    /// 调小它就能确定性地覆盖「句柄表过大」这条失败路径。
    /// </summary>
    internal static int MaxSnapshotBufferBytes { get; set; } = FileLockNative.MaxHandleBufferSize;

    /// <summary>带原因码的扫描失败：界面把 Failure/FailureCode 翻成文案，不解析异常消息。</summary>
    private sealed class ScanFailureException(FileLockScanFailure failure, int code, string message) : Exception(message)
    {
        public FileLockScanFailure Failure { get; } = failure;
        public int Code { get; } = code;
    }

    public static Task<FileLockScanResult> ScanAsync(string path, CancellationToken cancellationToken, IProgress<string>? progress = null)
        => Task.Run(() => Scan(path, cancellationToken, progress), cancellationToken);

    /// <summary>
    /// 扫描目标文件/目录的占用者。
    ///
    /// 过滤层次（越靠前越便宜，把可能阻塞的调用留到最后）：
    /// 1) ObjectTypeIndex == File；2) DuplicateHandle + GetFileType == DISK（这一层把管道、
    /// 设备、事件这类对象挡掉）；3) 已知卡死对象直接跳过（KnownHungObjects）；
    /// 4) 取名：**final path 是主路径**（GetFinalPathNameByHandleW 向文件系统要名字），
    /// 取不到的句柄才退回 NtQueryObject；5) 同卷前缀字符串预筛 + 路径匹配。
    ///
    /// 同卷预筛已从「查卷序列号」改成「比 final path 的卷前缀」：GetFileInformationByHandleEx(FileIdInfo)
    /// 在个别句柄上会卡死（实测），而卷前缀比较是纯字符串操作、不碰文件系统。
    /// 代价是不同卷前缀的句柄会被提前跳过——但它们本来就不可能命中目标，没有漏报。
    ///
    /// 刻意**不**按 GrantedAccess 跳过：0x0012019F 不是管道专属值，它就是
    /// FILE_GENERIC_READ|FILE_GENERIC_WRITE（读写打开），按它跳过会把 Excel / Word /
    /// FileStream(ReadWrite) 这类最常见的占用静默漏报——防卡死由第 2 层负责，与访问权限无关。
    ///
    /// 已知限制（v1 刻意不做）：
    /// - 不检测内存映射镜像（Section 对象）：运行中的 exe / 已加载的 DLL 占不出；
    /// - 不开启 SeDebugPrivilege：打不开的进程跳过并计入 SkippedProcesses；
    /// - 个别句柄上的文件系统查询会卡死（实测某驱动句柄）：卡住的那一个句柄会被跳过并从其后续跑
    ///   （计入 GuardedHandles），卡死的对象会被登记、后续扫描直接跳过；不调 TerminateThread，
    ///   代价是首次发现时可能泄漏一个后台线程与一个副本句柄。
    /// </summary>
    public static FileLockScanResult Scan(string path, CancellationToken cancellationToken, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new FileLockScanResult { Error = FileLockScanError.EmptyPath };

        string raw = path.Trim();
        bool isDirectory = Directory.Exists(raw);
        if (!isDirectory && !File.Exists(raw))
            return new FileLockScanResult { Error = FileLockScanError.NotFound };

        // 1. 用目标路径打开一个自己的句柄：既拿目标的内核设备名，也用来定位
        //    本启动周期的 File 对象类型索引（ObjectTypeIndex 每次开机稳定）。
        IntPtr ownHandle = FileLockNative.CreateFileW(
            raw, 0, FileLockNative.ShareAll, IntPtr.Zero,
            FileLockNative.OpenExisting, FileLockNative.FileFlagBackupSemantics, IntPtr.Zero);

        if (ownHandle == FileLockNative.InvalidHandleValue)
        {
            int lastError = Marshal.GetLastWin32Error();
            return new FileLockScanResult
            {
                Error = FileLockScanError.ResolveFailed,
                Failure = FileLockScanFailure.CreateFileFailed,
                FailureCode = lastError,
                ErrorDetail = $"CreateFileW 失败（错误码 {lastError}）"
            };
        }

        string targetFinalPath;
        string targetKernelName;
        ushort fileTypeIndex;
        try
        {
            if (FileLockNative.GetFileType(ownHandle) != FileLockNative.FileTypeDisk)
            {
                return new FileLockScanResult
                {
                    Error = FileLockScanError.ResolveFailed,
                    Failure = FileLockScanFailure.NotDiskFile,
                    ErrorDetail = "目标不是磁盘上的文件或目录"
                };
            }

            // 主匹配名 = final path（\\?\C:\...）：走文件系统解析，不像 NtQueryObject 那样会阻塞。
            // 极少数句柄上文件系统解析会失败，此时按用户给的路径归一化兜底（通常等价）。
            targetFinalPath = QueryFinalPath(ownHandle) ?? "";
            if (targetFinalPath.Length == 0)
            {
                try { targetFinalPath = @"\\?\" + Path.GetFullPath(raw); }
                catch { targetFinalPath = ""; }
            }

            // 兜底匹配名 = 内核设备名（\Device\HarddiskVolumeN\...）：仅当某个候选句柄取不到
            // final path 时才会用到。两个名字都拿不到才算解析失败。
            targetKernelName = QueryObjectName(ownHandle) ?? "";
            if (targetFinalPath.Length == 0 && targetKernelName.Length == 0)
            {
                return new FileLockScanResult
                {
                    Error = FileLockScanError.ResolveFailed,
                    Failure = FileLockScanFailure.DeviceNameUnavailable,
                    ErrorDetail = "无法解析目标路径"
                };
            }

            // 句柄表查询在这里（解析阶段）就可能失败（上限 / NtQuerySystemInformation）：
            // 必须折成带原因码的结果，否则异常会逃出 Scan，界面只能贴异常消息原文
            // ——英文界面里就会出现中文散文（review 发现 ④ 的主要失败路径）。
            try
            {
                fileTypeIndex = ResolveFileTypeIndex(ownHandle);
            }
            catch (ScanFailureException ex)
            {
                Debug.WriteLine($"[FileLock] 解析 File 类型索引失败: {ex.Message}");
                return new FileLockScanResult
                {
                    Error = FileLockScanError.Failed,
                    Failure = ex.Failure,
                    FailureCode = ex.Code,
                    ErrorDetail = ex.Message
                };
            }

            if (fileTypeIndex == 0)
            {
                return new FileLockScanResult
                {
                    Error = FileLockScanError.ResolveFailed,
                    Failure = FileLockScanFailure.ObjectTypeIndexUnavailable,
                    ErrorDetail = "无法确定 File 对象类型索引"
                };
            }

            // 同卷预筛不再查卷序列号：GetFileInformationByHandleEx(FileIdInfo) 在个别句柄上
            // 会卡死（实测本机某驱动句柄），改用 final path 的卷前缀做纯字符串比较。
        }
        finally
        {
            // 探测用的句柄必须关掉，否则本进程会被算成占用者（自伤式误报）。
            FileLockNative.CloseHandle(ownHandle);
        }

        // 匹配与显示都用规范化后的 final path；同卷预筛用它前两段的字符串前缀（C: 或 \\server\share）。
        targetFinalPath = NormalizeFinalPath(targetFinalPath);
        string targetVolumePrefix = VolumePrefixOf(targetFinalPath);

        var deviceMap = BuildDeviceMap();
        var state = new ScanState(targetFinalPath, targetKernelName, targetVolumePrefix, isDirectory, fileTypeIndex, deviceMap);

        try
        {
            RunWithWatchdog(state, cancellationToken, progress);
        }
        catch (OperationCanceledException)
        {
            // 目前 RunWithWatchdog 自己不会抛 OCE（取消只在分片线程里检查并抛出），
            // 但这条分支也要把主线程的名额还回去：将来若在循环里直接抛，才不会永久漏掉快照。
            state.ReleaseSnapshotOwner();
            throw;
        }
        catch (Exception ex)
        {
            // 主线程上的失败（句柄表查询 / 缓冲上限）：折成带原因码的结果，
            // 让界面走本地化文案，而不是把异常消息原样贴出来。
            Debug.WriteLine($"[FileLock] 扫描前置阶段失败: {ex}");
            state.ReleaseSnapshotOwner();
            return new FileLockScanResult
            {
                Error = FileLockScanError.Failed,
                Failure = (ex as ScanFailureException)?.Failure ?? FileLockScanFailure.Unexpected,
                FailureCode = (ex as ScanFailureException)?.Code ?? 0,
                ErrorDetail = ex.Message
            };
        }

        // 取消与看门狗放弃都可能发生在工作线程还在跑的时候，这里统一按「已取消」上抛。
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);

        var error = FileLockScanError.None;
        var failure = FileLockScanFailure.None;
        int failureCode = 0;
        string? detail = null;
        if (state.Error is OperationCanceledException canceled)
            throw canceled;
        if (state.Error is not null)
        {
            error = FileLockScanError.Failed;
            detail = state.Error.Message;
            failure = (state.Error as ScanFailureException)?.Failure ?? FileLockScanFailure.Unexpected;
            failureCode = (state.Error as ScanFailureException)?.Code ?? 0;

            // 意外失败的详情不再上屏（界面走本地化文案），至少留一条日志便于排查
            Debug.WriteLine($"[FileLock] 扫描失败({failure}): {state.Error}");
        }

        return new FileLockScanResult
        {
            Entries = state.BuildEntries(),
            ScannedHandles = state.ScannedHandles,
            SkippedProcesses = state.SkippedProcesses,
            GuardedHandles = state.GuardedHandles,
            Truncated = state.Truncated,
            Error = error,
            Failure = failure,
            FailureCode = failureCode,
            ErrorDetail = detail
        };
    }

    /// <summary>
    /// 取一份句柄表快照，切成若干片交给多个工作线程并行枚举，主线程做看门狗。
    ///
    /// 为什么要分片 + 续跑：NtQueryObject 在极少数句柄上会长时间阻塞（实测某显卡驱动持有的
    /// 一个读句柄能卡住 10 秒以上）。只有一个工作线程时，一个卡住的句柄会让**整轮扫描**
    /// 拿不到任何结果；分片后只损失它所在的那一片。更进一步：卡住时能读到该片**卡在哪一个
    /// 句柄**，于是把那个句柄记进 GuardedHandles 并从它之后续跑，最终只损失这一个句柄。
    ///
    /// 不调 TerminateThread（会破坏 CLR 运行时），卡住的线程被直接放弃——代价是可能
    /// 泄漏一个后台线程与它持有的少量句柄，换来的是进程绝不假死。
    /// </summary>
    private static void RunWithWatchdog(ScanState state, CancellationToken cancellationToken, IProgress<string>? progress)
    {
        IntPtr buffer = QueryHandleTable(out int count);
        state.AttachSnapshotBuffer(buffer);

        int workerCount = Math.Clamp(Environment.ProcessorCount, 2, 4);
        state.StartInitialSlices(buffer, count, workerCount, cancellationToken, progress);

        var stopwatch = Stopwatch.StartNew();
        int lastProcessed = -1;
        long lastChangeMs = 0;
        int recoveries = 0;

        while (state.PendingRunners > 0 && stopwatch.ElapsedMilliseconds < HardTimeoutMs)
        {
            Thread.Sleep(WatchdogTickMs);

            // 取消优先：工作线程可能正卡在 NtQueryObject 里，看门狗负责让调用方及时返回。
            if (cancellationToken.IsCancellationRequested) break;

            int processed = state.Processed;
            if (processed != lastProcessed)
            {
                lastProcessed = processed;
                lastChangeMs = stopwatch.ElapsedMilliseconds;
                continue;
            }

            long stalledMs = stopwatch.ElapsedMilliseconds - lastChangeMs;

            // 只剩一个分片没动静时基本可以断定它卡住了，不必等满 StallTimeout。
            long limit = state.PendingRunners <= 1 ? IdleAbandonMs : StallTimeoutMs;
            if (stalledMs < limit) continue;

            if (recoveries < MaxStallRecoveries)
            {
                int resumed = state.RecoverStalled(buffer, count, cancellationToken, progress);
                if (resumed > 0)
                {
                    recoveries += resumed;
                    lastChangeMs = stopwatch.ElapsedMilliseconds;
                    continue;
                }
            }

            // 没法再续跑。是真截断还是「只剩卡在片尾的那几个句柄没结论」，统一在循环后用
            // PendingRunners 与 ExhaustedRunners 的计数判定（见下）。
            break;
        }

        // 还在跑的分片若都已登记为「卡在片尾 / 已被续跑覆盖」（ExhaustedRunners），
        // 说明整张句柄表没有别的缺口：缺的只是它们正处理的那几个句柄的结论，
        // GuardedHandles 已如实计数，结果摘要会提示——不该把整次扫描标成「被截断」。
        state.Truncated = state.PendingRunners > state.ExhaustedRunners;
        state.ReleaseSnapshotOwner();
    }

    /// <summary>
    /// 路径的卷前缀（盘符路径 C: ；UNC 路径 \\server\share）。用于「同卷」快速预筛：
    /// 纯字符串操作，不碰文件系统——原来的卷序列号查询（GetFileInformationByHandleEx(FileIdInfo)）
    /// 在个别句柄上会卡死，已删除。
    /// </summary>
    internal static string VolumePrefixOf(string path)
    {
        string p = NormalizeFinalPath(path);
        if (p.StartsWith(@"\\", StringComparison.Ordinal))          // UNC：\\server\share\...
        {
            int first = p.IndexOf('\\', 2);
            if (first < 0) return p;
            int second = p.IndexOf('\\', first + 1);
            return second < 0 ? p : p[..second];
        }

        return p.Length >= 2 && p[1] == ':' ? p[..2] : "";
    }

    // ---------- 卡死句柄的记账 ----------

    /// <summary>
    /// 已知会卡死的句柄（pid + 句柄值）。实测本机 **RadeonSoftware** 持有的一个读句柄对**任何**
    /// 文件系统查询（GetFileInformationByHandleEx / GetFinalPathNameByHandleW）都会挂；而每次
    /// 「发现—跳过」都会在我们进程里泄漏一个副本句柄，副本下次扫描又成了新的卡死项
    /// （实测同一进程内连扫 GuardedHandles 会 1 → 2 → 4 → 8 地涨）。登记之后，后续扫描在查询
    /// 之前就跳过，同一进程内的规模被限制为「每个卡死句柄只发现一次」。
    ///
    /// 为什么不拿 Object 指针当身份：句柄表快照里**外部进程**条目的 Object 字段读出来是 0
    /// （内核只给自己进程的条目填对象指针）。pid + 句柄值在句柄存活期间稳定——我们自己泄漏的
    /// 副本恰好把那些值钉住了，不会被系统回收给别的句柄；再叠加 ObjectTypeIndex 相等做二次确认，
    /// 降低「句柄值被回收成另一个同号句柄」时的误跳概率。误跳会如实计入 GuardedHandles。
    /// </summary>
    private static readonly ConcurrentDictionary<(int Pid, ulong Handle), ushort> KnownHungHandles = new();

    /// <summary>
    /// 已知会卡死的**对象**（Object 指针）。只对我们自己进程的条目可读——泄漏在外的副本句柄
    /// 正是我们自己的，所以第一次在我们这边卡住时就能读到它，之后凡是引用同一对象的句柄
    /// （包括我们下次扫描才会碰到的那些副本）一次性跳过。对象被我们持有，指针不会被回收。
    /// </summary>
    private static readonly ConcurrentDictionary<long, byte> KnownHungObjects = new();

    /// <summary>防御上限：真出问题时也不让登记表无限长大（超限只是不再免疫，仍如实计数）。</summary>
    private const int MaxKnownHungHandles = 256;

    private static bool IsKnownHungHandle(int pid, ulong handleValue, ushort objectTypeIndex, IntPtr kernelObject)
        => (KnownHungHandles.TryGetValue((pid, handleValue), out ushort knownType)
                && knownType == objectTypeIndex)
            || (kernelObject != IntPtr.Zero && KnownHungObjects.ContainsKey(kernelObject.ToInt64()));

    /// <summary>把「卡住的那个分片正在处理的句柄」登记下来（索引必须落在句柄表内）。</summary>
    private static void RememberHungObject(IntPtr buffer, int count, int index)
    {
        if (index < 0 || index >= count) return;
        if (KnownHungHandles.Count >= MaxKnownHungHandles) return;

        var entry = Marshal.PtrToStructure<FileLockNative.SystemHandleTableEntryInfoEx>(
            EntryPointer(buffer, index, Marshal.SizeOf<FileLockNative.SystemHandleTableEntryInfoEx>()));
        int pid = unchecked((int)entry.UniqueProcessId.ToUInt64());
        ulong handle = entry.HandleValue.ToUInt64();
        if (handle == 0) return;

        if (KnownHungHandles.TryAdd((pid, handle), entry.ObjectTypeIndex))
            Debug.WriteLine($"[FileLock] 登记卡死句柄 pid={pid} handle=0x{handle:X}"
                + $"（type={entry.ObjectTypeIndex}）：后续扫描直接跳过");

        // 能读到对象指针就一并登记（管理员上下文里自家进程的条目才读得到）：下次扫描里指向同一对象
        // 的句柄全部跳过。对象被我们泄漏的副本持有，指针不会被回收。
        if (entry.Object != IntPtr.Zero && KnownHungObjects.TryAdd(entry.Object.ToInt64(), 0))
            Debug.WriteLine($"[FileLock] 登记卡死对象 0x{entry.Object.ToInt64():X}（同一对象的句柄后续一并跳过）");
    }

    // ---------- 匹配（internal static，便于单测） ----------

    /// <summary>
    /// 判断候选句柄名是否指向目标。主路径双方都是 final path（\\?\C:\...），兜底路径双方都是
    /// 内核设备名（\Device\HarddiskVolumeN\...）——比较只做相等与前缀判断，与名字的形式无关，
    /// 也因此不受盘符别名影响。
    /// 文件：完全相等（不区分大小写）；
    /// 目录：相等或目标前缀 + '\\'——必须带分隔符，否则 "\...\dir" 会误配 "\...\directory"。
    /// </summary>
    internal static bool MatchesTargetPath(string targetKernelName, string candidateName, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetKernelName) || string.IsNullOrWhiteSpace(candidateName))
            return false;

        string target = targetKernelName.TrimEnd('\\');
        string candidate = candidateName.TrimEnd('\\');

        if (candidate.Equals(target, StringComparison.OrdinalIgnoreCase))
            return true;

        return isDirectory
            && candidate.StartsWith(target + "\\", StringComparison.OrdinalIgnoreCase);
    }

    // ---------- 结果条目（internal static，便于单测） ----------

    /// <summary>
    /// 生成交给外部的条目。路径列表必须在这里拷成快照——被放弃的分片线程仍可能 AddHit，
    /// 而 UI 在扫描返回后会直接枚举 LockedPaths。
    /// </summary>
    internal static FileLockEntry SnapshotEntry(int pid, string name, string path, IReadOnlyList<string> paths, DateTime? startUtc)
        => new()
        {
            ProcessId = pid,
            ProcessName = name,
            ProcessPath = path,
            StartTimeUtc = startUtc,
            LockedPaths = paths.ToArray()
        };

    // ---------- 结束进程前的身份核对（internal static，便于单测） ----------

    /// <summary>
    /// 核对 PID 现在指向的进程是否还是扫描时的那一个。确认对话框期间目标可能已退出、
    /// PID 可能被系统复用，直接按 PID 结束会误杀无关进程。
    ///
    /// 判定规则：双方都有值才比较（进程名忽略大小写、映像路径忽略大小写、启动时间容差 1 秒）；
    /// 至少完成一项比较且全部通过才算 Match。名字是 "PID:1234" 兜底值时跳过该项——
    /// 它是「当时读不到名字」的标记，不是真实进程名。
    /// </summary>
    internal static ProcessIdentityCheck VerifyProcessIdentity(
        int pid, string expectedName, string expectedPath, DateTime? expectedStartUtc)
    {
        if (pid <= 4)
            return ProcessIdentityCheck.Mismatch;   // System / Idle 不是工具的目标

        string actualName;
        DateTime? actualStartUtc;
        try
        {
            using var process = Process.GetProcessById(pid);
            actualName = process.ProcessName;
            try { actualStartUtc = process.StartTime.ToUniversalTime(); }
            catch { actualStartUtc = null; }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return ProcessIdentityCheck.Exited;     // 进程已经没了
        }
        catch
        {
            return ProcessIdentityCheck.Mismatch;   // 打不开（权限 / 受保护）：信息不足，保守拒绝
        }

        int compared = 0;

        if (!string.IsNullOrEmpty(expectedName)
            && !expectedName.StartsWith(ProcessNameFallbackPrefix, StringComparison.Ordinal))
        {
            compared++;
            if (!string.Equals(expectedName, actualName, StringComparison.OrdinalIgnoreCase))
                return ProcessIdentityCheck.Mismatch;
        }

        string actualPath = QueryProcessPath(pid);
        if (!string.IsNullOrEmpty(expectedPath) && !string.IsNullOrEmpty(actualPath))
        {
            compared++;
            if (!string.Equals(expectedPath, actualPath, StringComparison.OrdinalIgnoreCase))
                return ProcessIdentityCheck.Mismatch;
        }

        if (expectedStartUtc is { } expected && actualStartUtc is { } actual)
        {
            compared++;
            if (Math.Abs((expected - actual).TotalSeconds) > 1)
                return ProcessIdentityCheck.Mismatch;
        }

        // 一项都没比成 = 扫到的信息不足以确认身份，宁可拒绝也不能闭眼杀
        return compared > 0 ? ProcessIdentityCheck.Match : ProcessIdentityCheck.Mismatch;
    }

    // ---------- 续跑决策（internal static，便于单测） ----------

    /// <summary>
    /// 决定一个卡住的分片是否、从哪里续跑。
    /// 已放弃的分片不再续跑：它的剩余区间早就交给了新分片，再续一次会让同一区间被反复重扫，
    /// 并把 GuardedHandles 重复计数（摘要里「因卡死跳过 N 个句柄」会虚高）。
    /// </summary>
    internal static bool TryPlanResume(int start, int end, int index, bool abandoned, out int resumeFrom)
    {
        resumeFrom = 0;
        if (abandoned) return false;
        resumeFrom = Math.Max(index + 1, start);
        return resumeFrom < end;
    }

    // ---------- 句柄表 ----------

    /// <summary>
    /// 取一份句柄表快照。返回非托管缓冲，调用方负责 FreeHGlobal。
    /// 头 16 字节是 { NumberOfHandles, Reserved }，其后是变长数组。
    /// </summary>
    private static IntPtr QueryHandleTable(out int handleCount)
    {
        int size = FileLockNative.InitialHandleBufferSize;

        while (true)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            int status = FileLockNative.NtQuerySystemInformation(
                FileLockNative.SystemExtendedHandleInformation, buffer, size, out int needed);

            if (status == 0)
            {
                var header = Marshal.PtrToStructure<FileLockNative.SystemHandleInformationExHeader>(buffer);
                handleCount = unchecked((int)header.NumberOfHandles.ToUInt64());
                return buffer;
            }

            Marshal.FreeHGlobal(buffer);

            if (status != FileLockNative.StatusInfoLengthMismatch)
                throw new ScanFailureException(FileLockScanFailure.NtQuerySystemInformationFailed, status,
                    $"NtQuerySystemInformation 失败：0x{status:X8}");

            if (needed <= size) needed = size * 2;
            if (needed > MaxSnapshotBufferBytes)
                throw new ScanFailureException(FileLockScanFailure.HandleTableTooLarge, needed, "句柄表超出上限，放弃扫描");

            size = needed;
        }
    }

    private static IntPtr EntryPointer(IntPtr buffer, int index, int entrySize)
        => buffer + Marshal.SizeOf<FileLockNative.SystemHandleInformationExHeader>() + index * entrySize;

    /// <summary>
    /// 在自己的句柄表条目里找到刚打开的目标句柄，取其 ObjectTypeIndex —— 即本启动周期
    /// File 对象的类型索引。后面靠纯整数比较就能砍掉 90%+ 的无关句柄。
    /// </summary>
    private static ushort ResolveFileTypeIndex(IntPtr ownHandle)
    {
        IntPtr buffer = QueryHandleTable(out int count);
        try
        {
            int entrySize = Marshal.SizeOf<FileLockNative.SystemHandleTableEntryInfoEx>();
            ulong ownPid = (ulong)Environment.ProcessId;
            ulong handleValue = unchecked((ulong)ownHandle.ToInt64());

            for (int i = 0; i < count; i++)
            {
                var entry = Marshal.PtrToStructure<FileLockNative.SystemHandleTableEntryInfoEx>(EntryPointer(buffer, i, entrySize));
                if (entry.UniqueProcessId.ToUInt64() == ownPid && entry.HandleValue.ToUInt64() == handleValue)
                    return entry.ObjectTypeIndex;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return 0;
    }

    // ---------- 句柄名（final path 主路径 + 对象名兜底） ----------

    /// <summary>
    /// 句柄的最终路径（\\?\C:\...）。走文件系统解析，不做对象管理器的同步查询，
    /// 因此不会像 NtQueryObject 那样在个别句柄上永久阻塞（本机实测 1 万+ 同卷磁盘句柄
    /// 全部即时返回）。取不到（已删除 / 权限受限）返回 null，由调用方退回对象名查询。
    /// </summary>
    private static string? QueryFinalPath(IntPtr handle)
    {
        var buffer = new StringBuilder(1024);
        uint length = FileLockNative.GetFinalPathNameByHandleW(
            handle, buffer, (uint)buffer.Capacity, FileLockNative.VolumeNameDos);
        if (length == 0) return null;

        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder((int)length + 1);
            length = FileLockNative.GetFinalPathNameByHandleW(
                handle, buffer, (uint)buffer.Capacity, FileLockNative.VolumeNameDos);
            if (length == 0 || length >= buffer.Capacity) return null;
        }

        return buffer.ToString();
    }

    /// <summary>
    /// 显示用：去掉 final path 的 \\?\ 前缀（\\?\UNC\srv\share → \\srv\share）。
    /// 只对盘符路径与 UNC 去前缀；卷 GUID 路径（\\?\Volume{…}\…）原样保留，
    /// 免得拼出一个并不存在的普通路径。
    /// </summary>
    internal static string NormalizeFinalPath(string finalPath)
    {
        if (finalPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + finalPath[8..];

        if (finalPath.StartsWith(@"\\?\", StringComparison.Ordinal)
            && finalPath.Length > 6 && char.IsLetter(finalPath[4]) && finalPath[5] == ':' && finalPath[6] == '\\')
            return finalPath[4..];

        return finalPath;
    }

    /// <summary>对象名兜底：NtQueryObject(ObjectNameInformation)。可能阻塞，只在 final path 取不到时调用。</summary>
    private static string? QueryObjectName(IntPtr handle)
    {
        int size = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            int status = FileLockNative.NtQueryObject(handle, FileLockNative.ObjectNameInformation, buffer, size, out int needed);
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
            Debug.WriteLine($"[FileLock] NtQueryObject 失败: {ex.Message}");
            return null;
        }
        finally
        {
            // 长度不足时会换成更大的缓冲，这里只释放当前这一个。
            try { Marshal.FreeHGlobal(buffer); } catch { /* 已释放 */ }
        }
    }

    // ---------- 显示路径映射 ----------

    /// <summary>设备名（\Device\HarddiskVolume3）→ 盘符（C:）。只服务兜底路径（NtQueryObject 的内核名）的显示。</summary>
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

                // QueryDosDevice 对盘符一般返回单个设备名；多字符串以 '\0' 分隔，取第一个。
                string device = target.ToString();
                int end = device.IndexOf('\0');
                if (end >= 0) device = device[..end];
                if (device.Length > 0) map[device] = letter;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileLock] 盘符映射失败（显示将退回内核名）: {ex.Message}");
        }
        return map;
    }

    private static string ToDisplayPath(string kernelName, IReadOnlyDictionary<string, string> deviceMap)
    {
        string bestDevice = "";
        string bestDrive = "";
        foreach (var (device, drive) in deviceMap)
        {
            if (!kernelName.StartsWith(device, StringComparison.OrdinalIgnoreCase)) continue;
            if (device.Length <= bestDevice.Length) continue;

            // 防止 \Device\HarddiskVolume1 误配 \Device\HarddiskVolume11
            if (kernelName.Length > device.Length && kernelName[device.Length] != '\\') continue;

            bestDevice = device;
            bestDrive = drive;
        }

        return bestDevice.Length == 0
            ? kernelName
            : bestDrive + kernelName[bestDevice.Length..];
    }

    // ---------- 进程信息 ----------

    internal static string QueryProcessPath(int pid)
    {
        IntPtr process = FileLockNative.OpenProcess((int)FileLockNative.ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero) return "";

        try
        {
            var builder = new StringBuilder(1024);
            int length = builder.Capacity;
            return FileLockNative.QueryFullProcessImageNameW(process, 0, builder, ref length)
                ? builder.ToString(0, length)
                : "";
        }
        catch
        {
            return "";
        }
        finally
        {
            FileLockNative.CloseHandle(process);
        }
    }

    /// <summary>进程名 + 映像路径 + 启动时间，按 PID 缓存（与 PortViewerService 的惯例一致）。</summary>
    private static (string Name, string Path, DateTime? StartUtc) GetProcessInfo(
        int pid, Dictionary<int, (string Name, string Path, DateTime? StartUtc)> cache)
    {
        if (cache.TryGetValue(pid, out var cached)) return cached;

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
                catch { /* 受保护进程读不到启动时间：身份核对退到名字 / 路径 */ }
            }
            catch
            {
                name = ProcessNameFallbackPrefix + pid;
            }
            info = (name, QueryProcessPath(pid), startUtc);
        }

        cache[pid] = info;
        return info;
    }

    // ---------- 枚举 ----------

    /// <summary>
    /// 扫描过程的可变状态。多个分片线程并行写入（结果集合用 <see cref="_resultLock"/> 保护），
    /// 看门狗线程读进度与分片状态；计数一律走 Interlocked/Volatile 保证可见性。
    /// </summary>
    private sealed class ScanState
    {
        private readonly string _targetFinalPath;
        private readonly string _targetKernelName;
        private readonly bool _isDirectory;
        private readonly ushort _fileTypeIndex;
        private readonly string _targetVolumePrefix;
        private readonly IReadOnlyDictionary<string, string> _deviceMap;

        private readonly Dictionary<int, (string Name, string Path, DateTime? StartUtc)> _processCache = new();
        private readonly HashSet<int> _skippedPids = new();
        private readonly Dictionary<int, EntryBuilder> _builders = new();

        /// <summary>
        /// 保护结果集合与错误字段。看门狗放弃等待后工作线程可能仍在跑，
        /// 主线程此时要读结果，所以写入与读取都必须串行化。
        /// </summary>
        private readonly object _resultLock = new();

        private int _processed;
        private int _pendingRunners;
        private Exception? _error;

        /// <summary>已启动的句柄区间分片；卡住时会从卡住位置之后续跑一段新的。</summary>
        private readonly List<Runner> _runners = new();
        private int _runnerSeq;

        public ScanState(string targetFinalPath, string targetKernelName, string targetVolumePrefix, bool isDirectory,
            ushort fileTypeIndex, IReadOnlyDictionary<string, string> deviceMap)
        {
            _targetFinalPath = targetFinalPath;
            _targetKernelName = targetKernelName;
            _targetVolumePrefix = targetVolumePrefix;
            _isDirectory = isDirectory;
            _fileTypeIndex = fileTypeIndex;
            _targetVolumePrefix = targetVolumePrefix;
            _deviceMap = deviceMap;
        }

        public int Processed => Volatile.Read(ref _processed);

        /// <summary>
        /// 仍在跑的分片数：0 表示全部收尾。看门狗循环与卡死判定用它；
        /// **快照能否释放改看 _snapshotOwners**（看门狗自己也算一个持有者）。
        /// </summary>
        public int PendingRunners => Volatile.Read(ref _pendingRunners);

        public Exception? Error
        {
            get { lock (_resultLock) return _error; }
        }

        public bool Truncated { get; set; }

        private int _scannedHandles;
        private int _guardedHandles;
        private int _exhaustedRunners;

        public int ScannedHandles => Volatile.Read(ref _scannedHandles);
        public int GuardedHandles => Volatile.Read(ref _guardedHandles);

        /// <summary>
        /// 已登记为「卡在片尾 / 已被续跑覆盖」的分片数：我们不再等它们，但它们的区间已经
        /// 扫完（缺的只是正处理的那一个句柄）。收尾判定用它区分真截断——见 RunWithWatchdog。
        /// 只增不减：是否截断只在循环结束的那一瞬间读一次。
        /// </summary>
        public int ExhaustedRunners => Volatile.Read(ref _exhaustedRunners);

        private void MarkRunnerExhausted() => Interlocked.Increment(ref _exhaustedRunners);

        public int SkippedProcesses
        {
            get { lock (_resultLock) return _skippedPids.Count; }
        }

        private void MarkSkipped(int pid)
        {
            lock (_resultLock) _skippedPids.Add(pid);
        }

        // ---------- 句柄表快照的所有权 ----------

        private IntPtr _snapshotBuffer;

        /// <summary>
        /// 快照持有者名额：主线程（看门狗随时可能再拉起续跑分片）算一个，每个分片线程算一个。
        /// **看门狗必须计入**：它准备续跑时会 Launch 新分片，若那时分片数恰好归零就释放了快照，
        /// 新分片会读到已释放的内存（use-after-free）。
        /// </summary>
        private int _snapshotOwners;

        /// <summary>快照是否已接管（0/1）。没接管过就没有账可还，避免误减成负数。</summary>
        private int _snapshotAttached;

        /// <summary>接管句柄表快照：主线程自己先占一个持有名额。</summary>
        public void AttachSnapshotBuffer(IntPtr buffer)
        {
            _snapshotBuffer = buffer;
            _snapshotOwners = 1;
            _snapshotAttached = 1;
            Interlocked.Increment(ref _liveSnapshotBuffers);
        }

        /// <summary>分片启动前占名额（在 Thread.Start 之前调用，保证不晚于任何释放）。</summary>
        private void AcquireSnapshotOwner() => Interlocked.Increment(ref _snapshotOwners);

        /// <summary>
        /// 一个持有者（主线程收尾 / 分片线程 finally）到期。名额归零才真正释放；
        /// Interlocked.Exchange 取走指针保证**恰好释放一次**（两次就是堆损坏）。
        /// 真正永久卡死的分片会让名额永远不归零——那一块快照只能留在原地，
        /// 与被放弃的线程同一取舍（见 RunWithWatchdog 的说明）。
        /// </summary>
        public void ReleaseSnapshotOwner()
        {
            if (Volatile.Read(ref _snapshotAttached) == 0) return;
            if (Interlocked.Decrement(ref _snapshotOwners) != 0) return;

            IntPtr buffer = Interlocked.Exchange(ref _snapshotBuffer, IntPtr.Zero);
            if (buffer == IntPtr.Zero) return;

            Marshal.FreeHGlobal(buffer);
            Interlocked.Decrement(ref _liveSnapshotBuffers);
        }

        /// <summary>把句柄表切成 workerCount 片，各自起一个线程枚举。</summary>
        public void StartInitialSlices(IntPtr buffer, int count, int workerCount,
            CancellationToken cancellationToken, IProgress<string>? progress)
        {
            int slice = (count + workerCount - 1) / workerCount;

            for (int w = 0; w < workerCount; w++)
            {
                int start = w * slice;
                int end = Math.Min(start + slice, count);
                if (start < end) Launch(buffer, count, start, end, cancellationToken, progress);
            }
        }

        /// <summary>
        /// 卡住恢复：每个没跑完的分片，把它卡住的那一个句柄记为「为防卡死而跳过」，
        /// 然后从该句柄之后续跑剩余区间。返回续跑的分片数（0 表示没法再恢复）。
        /// 已被放弃的分片不再进入候选（见 TryPlanResume）。
        /// </summary>
        public int RecoverStalled(IntPtr buffer, int count,
            CancellationToken cancellationToken, IProgress<string>? progress)
        {
            List<Runner> stalled;
            lock (_runners) stalled = _runners.Where(r => !r.Done).ToList();

            int resumed = 0;
            foreach (var runner in stalled)
            {
                int stuckAt = Volatile.Read(ref runner.Index);

                // 它卡在哪一个句柄上：把那个对象登记下来，后续扫描在查询之前就跳过。
                // 不登记的话，每跳过一次都会泄漏一个副本句柄，副本下次扫描又成为新的卡死项
                // （实测空闲三连扫的 GuardedHandles 会 1 → 2 → 4 地涨）。
                RememberHungObject(buffer, count, stuckAt);

                // 再把卡住的那个**副本句柄**收回来关掉：卡住的线程自己关不了（finally 永远跑不到），
                // 不回收就会永久泄漏、并在下一次扫描里变成新的卡死项（登记只能治「旧的那一个」）。
                IntPtr stuckHandle = Interlocked.Exchange(ref runner.QueryingHandle, IntPtr.Zero);
                if (stuckHandle != IntPtr.Zero)
                {
                    FileLockNative.CloseHandle(stuckHandle);
                    Debug.WriteLine($"[FileLock] 已回收卡死分片持有的副本句柄 0x{stuckHandle.ToInt64():X}");
                }

                // 逻辑放弃与物理完成必须分开：把仍卡着的线程提前标成 Done，看门狗会以为分片都收尾了，
                // 而它名下的快照持有名额永远不回还（快照再也释放不掉）。
                bool alreadyAbandoned = Interlocked.CompareExchange(ref runner.Abandoned, 1, 0) == 1;

                if (!TryPlanResume(runner.Start, runner.End, stuckAt, alreadyAbandoned, out int resumeFrom))
                {
                    // 没有剩余区间可扫：卡在片尾，或早先已被续跑覆盖。前者是本轮新放弃的分片，
                    // 它正处理的那个句柄确实没有结论，要计数；后者不重复计数。
                    MarkRunnerExhausted();
                    if (!alreadyAbandoned)
                        Interlocked.Increment(ref _guardedHandles);
                    continue;
                }

                // 记入 GuardedHandles：这个句柄因为会卡死被跳过了，用户有权知道可能漏报。
                Interlocked.Increment(ref _guardedHandles);
                Launch(buffer, count, resumeFrom, runner.End, cancellationToken, progress);
                resumed++;
            }

            return resumed;
        }

        private void Launch(IntPtr buffer, int count, int start, int end,
            CancellationToken cancellationToken, IProgress<string>? progress)
        {
            var runner = new Runner { Start = start, End = end, Index = start - 1 };
            lock (_runners) _runners.Add(runner);

            Interlocked.Increment(ref _pendingRunners);
            Interlocked.Increment(ref _activeScanRunners);

            // 先占快照名额再启动线程：释放以「名额归零」为准，新分片因此不可能在别人已释放后起跑。
            // 名额的获取与线程构造都放进 try：中途抛异常时名额必须一起还回去，否则快照再也释放不掉。
            try
            {
                AcquireSnapshotOwner();

                var thread = new Thread(() => RunRunner(runner, buffer, count, cancellationToken, progress))
                {
                    IsBackground = true,
                    Name = $"FileLockScan{Interlocked.Increment(ref _runnerSeq)}"
                };

                thread.Start();
            }
            catch
            {
                // 线程没起来（含构造失败）：把名额与计数还回去，否则快照再也没人释放
                runner.Done = true;
                Interlocked.Decrement(ref _pendingRunners);
                Interlocked.Decrement(ref _activeScanRunners);
                ReleaseSnapshotOwner();
                throw;
            }
        }

        private void RunRunner(Runner runner, IntPtr buffer, int count,
            CancellationToken cancellationToken, IProgress<string>? progress)
        {
            try
            {
                EnumerateRange(runner, buffer, count, cancellationToken, progress);
            }
            catch (Exception ex)
            {
                lock (_resultLock) _error ??= ex;
            }
            finally
            {
                // 先置 Done 再减计数：PendingRunners 归零时保证没有任何线程还在读缓冲。
                runner.Done = true;
                Interlocked.Decrement(ref _pendingRunners);

                // 先交还快照名额、再减「活动分片数」：测试用 ActiveScanRunners 归零后立刻断言
                // 快照已释放，顺序反了会读到「分片已清空、但还没释放」的中间态。
                ReleaseSnapshotOwner();
                Interlocked.Decrement(ref _activeScanRunners);
            }
        }

        /// <summary>一个句柄区间分片。[Index] 是它当前处理到的位置，卡住时据此定位。</summary>
        private sealed class Runner
        {
            public int Start;
            public int End;
            public int Index;
            public volatile bool Done;

            /// <summary>
            /// 1 = 剩余区间已交给新分片，不再参与续跑（见 TryPlanResume）。
            /// 与 [Done] 必须分开：这里只表示「逻辑上放弃」，物理线程可能仍卡在原生调用里。
            /// 提前把卡住的线程标成 Done 会让看门狗以为分片都收尾了，而那个线程名下的
            /// 快照持有名额永远不回还——整块快照再也释放不掉。
            /// 仅用 Interlocked 访问。
            /// </summary>
                            public int Abandoned;

                            /// <summary>
                            /// 分片当前正在查询的那个副本句柄（查询前发布，查询返回后由自己或看门狗取走）。
                            /// 看门狗发现分片卡死时会把它**关掉**——这个副本是我们复制出来的，卡住的线程
                            /// 的 finally 永远跑不到，不回收就会永久泄漏，下一次扫描里又成为一个新的卡死项。
                            /// 谁拿到发布槽谁负责关闭（恰好一次）：重复关可能撞上句柄值被回收、误关别人的句柄。
                            /// 仅用 Interlocked/Volatile 访问。
                            /// </summary>
                            public IntPtr QueryingHandle;
        }

        private void EnumerateRange(Runner runner, IntPtr buffer, int totalCount,
            CancellationToken cancellationToken, IProgress<string>? progress)
        {
            int entrySize = Marshal.SizeOf<FileLockNative.SystemHandleTableEntryInfoEx>();
            IntPtr currentProcess = FileLockNative.GetCurrentProcess();

            int lastReported = 0;

            for (int i = runner.Start; i < runner.End; i++)
            {
                TestPerHandleHook?.Invoke(i);
                Volatile.Write(ref runner.Index, i);
                Interlocked.Increment(ref _scannedHandles);
                Interlocked.Increment(ref _processed);

                if ((i & 0x3FF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (i - lastReported >= 0x3FF)
                    {
                        lastReported = i;
                        progress?.Report(string.Format(
                            LocalizationService.L("FileLock_Progress", "已检查 {0} / {1} 个句柄"),
                            Processed, totalCount));
                    }
                }

                    var entry = Marshal.PtrToStructure<FileLockNative.SystemHandleTableEntryInfoEx>(
                        EntryPointer(buffer, i, entrySize));

                    // 第 2 层：只留 File 类型（纯整数比较，砍掉 90%+）
                    if (entry.ObjectTypeIndex != _fileTypeIndex) continue;

                    int pid = unchecked((int)entry.UniqueProcessId.ToUInt64());
                    if (pid <= 4)
                    {
                        MarkSkipped(pid);
                        continue;
                    }

                    // 已知会卡死的句柄（pid + 句柄值）：连打开/查询都不做，直接跳过（计入 GuardedHandles，用户可见）。
                    // 登记发生在真的卡过之后——见 RecoverStalled / RememberHungObject。
                    if (IsKnownHungHandle(pid, entry.HandleValue.ToUInt64(), entry.ObjectTypeIndex, entry.Object))
                    {
                        Interlocked.Increment(ref _guardedHandles);
                        continue;
                    }

                    IntPtr sourceProcess = FileLockNative.OpenProcess((int)FileLockNative.ProcessDupHandle, false, pid);
                    if (sourceProcess == IntPtr.Zero)
                    {
                        MarkSkipped(pid);   // 受保护/已退出进程打不开：如实计数
                        continue;
                    }

                    try
                    {
                        IntPtr handleValue = new(unchecked((long)entry.HandleValue.ToUInt64()));
                        if (!FileLockNative.DuplicateHandle(sourceProcess, handleValue, currentProcess,
                                out IntPtr local, 0, false, FileLockNative.DuplicateSameAccess))
                        {
                            MarkSkipped(pid);
                            continue;
                        }

                        try
                        {
                            // 把「正在查询的句柄」发布出去：一旦这里卡死，看门狗会替我们把它关掉
                            //（不回收的话这个副本会永久泄漏，下次扫描又成为新的卡死项）。
                            Volatile.Write(ref runner.QueryingHandle, local);

                            // 第 3 层：只处理磁盘文件/目录（管道、事件、互斥体等在这里被剔除）。
                            // 注意：**磁盘句柄也可能卡**——实测某驱动持有的句柄对任何文件系统查询
                            // 都会挂，所以这层只是降低查询量，不能当「屏障」；真正的兜底是分片看门狗
                            // 加 KnownHungObjects 登记（卡过的对象下次直接跳过）。
                            if (FileLockNative.GetFileType(local) != FileLockNative.FileTypeDisk) continue;

                            // 第 4 层：取名。final path 是主路径（GetFinalPathNameByHandleW）；
                            // 取不到（已删除 / 权限受限的句柄）才退回 NtQueryObject 兜底。
                            string? rawFinalPath = QueryFinalPath(local);
                            if (!string.IsNullOrEmpty(rawFinalPath))
                            {
                                string finalPath = NormalizeFinalPath(rawFinalPath);

                                // 第 5 层：同卷预筛 + 匹配。纯字符串比较——不再查卷序列号，
                                // 那一步（GetFileInformationByHandleEx(FileIdInfo)）本身会卡死。
                                if (!finalPath.StartsWith(_targetVolumePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                                if (!MatchesTargetPath(_targetFinalPath, finalPath, _isDirectory)) continue;
                                AddHit(pid, finalPath);
                                continue;
                            }

                            // 兜底路径：对象名（内核设备名）对目标的内核名
                            if (_targetKernelName.Length == 0) continue;
                            string? name = QueryObjectName(local);
                            if (name is null) continue;
                            if (!MatchesTargetPath(_targetKernelName, name, _isDirectory)) continue;

                            AddHit(pid, ToDisplayPath(name, _deviceMap));
                        }
                        finally
                        {
                            // 谁取到发布槽谁负责关闭：看门狗已经替我们关过（返回 0）就不重复关
                            if (Interlocked.Exchange(ref runner.QueryingHandle, IntPtr.Zero) != IntPtr.Zero)
                                FileLockNative.CloseHandle(local);
                        }
                    }
                    finally
                    {
                        FileLockNative.CloseHandle(sourceProcess);
                    }
                }
        }

        private void AddHit(int pid, string displayPath)
        {
            lock (_resultLock)
            {
                if (!_builders.TryGetValue(pid, out var builder))
                {
                    var (name, path, startUtc) = GetProcessInfo(pid, _processCache);
                    builder = new EntryBuilder(pid, name, path, startUtc);
                    _builders[pid] = builder;
                }
                builder.Add(displayPath);
            }
        }

        public IReadOnlyList<FileLockEntry> BuildEntries()
        {
            lock (_resultLock)
            {
                return _builders.Values
                    .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(b => b.ProcessId)
                    .Select(b => SnapshotEntry(b.ProcessId, b.Name, b.Path, b.Paths, b.StartTimeUtc))
                    .ToList();
            }
        }

        private sealed class EntryBuilder(int pid, string name, string path, DateTime? startUtc)
        {
            private readonly List<string> _paths = [];
            private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

            public int ProcessId { get; } = pid;
            public string Name { get; } = name;
            public string Path { get; } = path;
            public DateTime? StartTimeUtc { get; } = startUtc;
            public IReadOnlyList<string> Paths => _paths;

            public void Add(string displayPath)
            {
                if (_seen.Add(displayPath)) _paths.Add(displayPath);
            }
        }
    }
}
