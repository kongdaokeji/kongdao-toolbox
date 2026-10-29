using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace TubaWinUi3.Services.EnvVars;

/// <summary>
/// 一次写入 / 恢复的结果。失败时只产出<b>代码 + 参数</b>（界面按代码查资源键渲染中英文案），
/// 原始异常文本放在 <see cref="Detail"/> 里且<b>只用于日志</b>，不直接展示给用户。
/// </summary>
public sealed class EnvApplyResult
{
    public bool Succeeded { get; init; }

    /// <summary>失败原因代码；成功时为 <see cref="EnvFailureCode.None"/>。</summary>
    public EnvFailureCode FailureCode { get; init; }

    /// <summary>涉及的变量名（用户输入，可原样展示）或注册表项路径；与失败原因无关时为 null。</summary>
    public string? FailureName { get; init; }

    /// <summary>写入 / 校验失败后自动回滚的结果；没走回滚路径时是 NotAttempted。</summary>
    public EnvRollbackState Rollback { get; init; }

    /// <summary>原始异常 / 诊断文本，只用于日志（<see cref="Fail"/> 会顺手写到 Debug 输出）。</summary>
    public string? Detail { get; init; }

    /// <summary>
    /// 本次的写前快照路径。<b>备份创建失败时整批写入会直接取消</b>（<see cref="Succeeded"/> 为 false），
    /// 所以成功结果里它必然有值（「没有改动」的空批次除外）。
    /// </summary>
    public string? BackupPath { get; init; }

    /// <summary>失败是权限问题（界面据此提示「需要管理员权限」）。</summary>
    public bool NeedsElevation { get; init; }

    public int ChangedCount { get; init; }

    public static EnvApplyResult Ok(string? backupPath, int changed)
        => new() { Succeeded = true, BackupPath = backupPath, ChangedCount = changed };

    public static EnvApplyResult Fail(
        EnvFailureCode code,
        string? name = null,
        bool needsElevation = false,
        EnvRollbackState rollback = EnvRollbackState.NotAttempted,
        string? detail = null,
        string? backupPath = null)
    {
        if (detail is not null)
            Debug.WriteLine($"[EnvVars] 操作失败：{code}（name={name ?? "-"}）{detail}");

        return new()
        {
            FailureCode = code,
            FailureName = name,
            NeedsElevation = needsElevation,
            Rollback = rollback,
            Detail = detail,
            BackupPath = backupPath,
        };
    }
}

/// <summary>
/// 环境变量的注册表读写。
///
/// 口径（与 PowerToys Environment Variables 一致，逐条踩过坑）：
/// - <b>不走 <c>Environment.SetEnvironmentVariable</c></b>：它每写一个变量就有约 1 秒超时，
///   批量应用一个配置集就是「变量数 × 1s」；而且它把值写成 REG_SZ，<c>%VAR%</c> 从此不再展开。
/// - 读值必须带 <see cref="RegistryValueOptions.DoNotExpandEnvironmentNames"/>，
///   否则注册表 API 会把 <c>%VAR%</c> 当场展开成当前进程的值，写回去就固化了。
/// - 写值：有显式类型（恢复备份时带上的快照 Kind）就按显式写，否则含 <c>%</c> 写 REG_EXPAND_SZ、
///   不含写 REG_SZ；值为 null 表示删除。
/// - 一律显式 <see cref="RegistryView.Registry64"/>：x86 构建下 HKLM 会被重定向到 Wow6432Node。
/// - 刷新走 <c>SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, …)</c>，
///   且只在整批写完后广播一次（单条写不广播）。
/// </summary>
public static class EnvironmentVariableService
{
    /// <summary>用户作用域：HKCU 下的相对键路径。</summary>
    private const string UserKeyPath = "Environment";

    /// <summary>系统作用域：HKLM 下的相对键路径。</summary>
    private const string SystemKeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    /// <summary>每个作用域保留的快照份数，超出按时间删最旧。</summary>
    private const int BackupRetention = 20;

    private const uint HWND_BROADCAST = 0xFFFF;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>广播时用的自定义 wParam：用来把自己发的广播（非 0）与外部改动（0）区分开。</summary>
    public const uint OwnBroadcastMarker = 0x12345;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutW(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    /// <summary>测试用：把数据目录指到临时目录（与 TimeSyncCatalog / GameTunnelCatalog 同一套约定）。</summary>
    public static string? DataDirOverride { get; set; }

    public static string DataDir
    {
        get
        {
            var dir = DataDirOverride ?? Path.Combine(ConfigManager.GetDataDir(), "EnvVars");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string BackupsDir
    {
        get
        {
            var dir = Path.Combine(DataDir, "backups");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string RegistryPathFor(EnvScope scope) => scope == EnvScope.User ? UserKeyPath : SystemKeyPath;

    public static string ScopeName(EnvScope scope) => scope == EnvScope.User ? "User" : "System";

    private static RegistryHive HiveFor(EnvScope scope)
        => scope == EnvScope.User ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;

    // ---------- 读 ----------

    /// <summary>读取一个作用域下的全部变量（按名称排序）。失败时返回失败代码与诊断文本（诊断只进日志）、列表为空。</summary>
    public static (List<EnvVarEntry> Entries, EnvFailureCode FailureCode, string? FailureName, string? Detail) ReadAll(EnvScope scope)
    {
        var entries = new List<EnvVarEntry>();

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(HiveFor(scope), RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(RegistryPathFor(scope));
            if (key is null)
                return (entries, EnvFailureCode.KeyUnavailable, RegistryPathFor(scope), null);

            foreach (var name in key.GetValueNames())
            {
                // 未命名默认值（值名为空）不是环境变量，跳过
                if (string.IsNullOrEmpty(name)) continue;
                entries.Add(ReadEntry(key, name));
            }

            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return (entries, EnvFailureCode.None, null, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EnvVars] 读取失败：{ex.Message}");
            return (entries, EnvFailureCode.ReadFailed, null, ex.Message);
        }
    }

    /// <summary>读取单个变量（大小写不敏感）；不存在返回 null。</summary>
    public static EnvVarEntry? ReadOne(EnvScope scope, string name)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(HiveFor(scope), RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(RegistryPathFor(scope));
            if (key is null) return null;
            return TryReadEntry(key, name, out _);
        }
        catch
        {
            return null;
        }
    }

    // ---------- 写 ----------

    /// <summary>
    /// 应用一批改动：先全部校验 → 不支持类型门禁 → 写前快照 → 逐条写 → 写后回读校验 → 广播一次。
    /// 校验不通过、命中不支持类型、快照写不出来：一律<b>一个字节都不写</b>、整批取消。
    /// 写入或回读校验中途失败：用本次快照<b>自动回滚</b>到修改前状态；回滚结果如实放进
    /// <see cref="EnvApplyResult.Rollback"/>，回滚失败时明确告知「部分修改可能仍然存在」。
    /// 广播失败只记诊断，既不算保存失败、也不回滚已成功的写入（见 <see cref="NotifyEnvironmentChange"/>）。
    /// </summary>
    public static EnvApplyResult Apply(EnvScope scope, IReadOnlyList<EnvVarChange> changes)
    {
        if (changes.Count == 0) return EnvApplyResult.Ok(null, 0);

        // 1) 先全部校验，任何一条不合法就整批不写
        foreach (var change in changes)
        {
            if (change.Kind != EnvChangeKind.Set) continue;

            var value = change.Value ?? "";
            var error = EnvVarRules.ValidateName(change.Name)
                        ?? EnvVarRules.ValidateValue(value)
                        ?? EnvVarRules.ValidateTotalLength(change.Name, value);
            if (error is { } code)
                return EnvApplyResult.Fail(code, name: change.Name);
        }

        EnvVarBackupFile? backup = null;
        string? backupPath = null;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(HiveFor(scope), RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(RegistryPathFor(scope), writable: true);
            if (key is null) return EnvApplyResult.Fail(EnvFailureCode.KeyUnavailable, name: RegistryPathFor(scope));

            // 2) 不支持的注册表类型（非 REG_SZ / REG_EXPAND_SZ）一律不许改写：这类值读出来只是
            //    字符串化的展示文本，写回去必然发生类型转换；宁可整批拒绝，也不静默把它变成 REG_SZ。
            try
            {
                foreach (var change in changes)
                {
                    var existing = TryReadEntry(key, change.Name, out _);
                    if (existing is { KindUnsupported: true })
                        return EnvApplyResult.Fail(EnvFailureCode.UnsupportedKind, name: existing.Name);
                }
            }
            catch (Exception ex)
            {
                return EnvApplyResult.Fail(EnvFailureCode.ReadFailed, detail: ex.Message);
            }

            // 3) 写前快照（只备份本次真正会被改动的变量名）。备份失败 = 立即中止：
            //    没有回滚点的写入不能做，绝不能把备份失败当普通警告继续往下写。
            var (written, path, backupError) = WriteBackup(scope, key, changes);
            if (backupError is not null)
                return EnvApplyResult.Fail(EnvFailureCode.BackupFailed, detail: backupError);
            backup = written;
            backupPath = path;

            // 4) 逐条写 + 5) 写后回读校验。放在内层 try 里是为了拿着 key 做自动回滚。
            try
            {
                foreach (var change in changes)
                {
                    GuardWriteInjection(change.Name);   // 测试用故障注入（生产恒为 null）

                    if (change.Kind == EnvChangeKind.Delete)
                    {
                        // throwOnMissingValue: false —— 本来就不存在的变量，删除算成功
                        key.DeleteValue(change.Name, throwOnMissingValue: false);
                        continue;
                    }

                    key.SetValue(change.Name, change.Value ?? "", RegistryKindFor(change));
                }

                foreach (var change in changes)
                {
                    var actual = TryReadEntry(key, change.Name, out _);

                    if (change.Kind == EnvChangeKind.Delete)
                    {
                        if (actual is not null)
                            return FailRolledBack(EnvFailureCode.DeleteFailed, change.Name, key, backup!, backupPath);
                        continue;
                    }

                    var expected = change.Value ?? "";
                    if (actual is null || !string.Equals(actual.Value, expected, StringComparison.Ordinal))
                        return FailRolledBack(
                            EnvFailureCode.VerifyFailed, change.Name, key, backup!, backupPath,
                            $"回读值不一致：期望「{expected}」，实际「{actual?.Value}」");

                    var expectedKind = RegistryKindFor(change).ToString();   // 与写入用同一决策函数，两边永远一致
                    if (!string.Equals(actual.Kind, expectedKind, StringComparison.Ordinal))
                        return FailRolledBack(
                            EnvFailureCode.VerifyFailed, change.Name, key, backup!, backupPath,
                            $"回读类型不一致：期望 {expectedKind}，实际 {actual.Kind}");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                return FailRolledBack(
                    EnvFailureCode.AccessDenied, name: null, key, backup!, backupPath, ex.Message, needsElevation: true);
            }
            catch (Exception ex)
            {
                return FailRolledBack(EnvFailureCode.WriteFailed, name: null, key, backup!, backupPath, ex.Message);
            }

            // 6) 整批写完才广播一次（尽力通知；失败只记诊断，不影响保存结果）
            NotifyEnvironmentChange();

            return EnvApplyResult.Ok(backupPath, changes.Count);
        }
        catch (UnauthorizedAccessException ex)
        {
            return EnvApplyResult.Fail(
                EnvFailureCode.AccessDenied, needsElevation: true, detail: ex.Message, backupPath: backupPath);
        }
        catch (Exception ex)
        {
            return EnvApplyResult.Fail(EnvFailureCode.WriteFailed, detail: ex.Message, backupPath: backupPath);
        }
    }

    /// <summary>
    /// 写入 / 校验失败后的收尾：用本次快照自动回滚，并把回滚结果如实放进返回值。
    /// 快照文件本身不动，用户仍可用「恢复上次备份」人工再试。
    /// </summary>
    private static EnvApplyResult FailRolledBack(
        EnvFailureCode code,
        string? name,
        RegistryKey key,
        EnvVarBackupFile backup,
        string? backupPath,
        string? detail = null,
        bool needsElevation = false)
    {
        var (ok, rollbackError) = TryRollback(key, backup);
        if (!ok)
            Debug.WriteLine($"[EnvVars] 自动回滚失败（{name ?? "批量写入"}）：{rollbackError}");

        return EnvApplyResult.Fail(
            code,
            name: name,
            needsElevation: needsElevation,
            rollback: ok ? EnvRollbackState.Succeeded : EnvRollbackState.Failed,
            detail: ok ? detail : $"{detail}；自动回滚失败：{rollbackError}",
            backupPath: backupPath);
    }

    /// <summary>
    /// 把本次快照里的内容写回注册表（自动回滚）。类型走与恢复同一条链（BuildRestoreChanges → RegistryKindFor），
    /// 不会把 REG_EXPAND_SZ 降级；不支持的注册表类型按规则跳过。尽力而为：失败返回错误文本。
    /// </summary>
    private static (bool Ok, string? Error) TryRollback(RegistryKey key, EnvVarBackupFile backup)
    {
        try
        {
            foreach (var change in EnvVarRules.BuildRestoreChanges(backup.Entries))
            {
                GuardWriteInjection(change.Name);

                if (change.Kind == EnvChangeKind.Delete)
                {
                    key.DeleteValue(change.Name, throwOnMissingValue: false);
                    continue;
                }

                key.SetValue(change.Name, change.Value ?? "", RegistryKindFor(change));
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// 测试用故障注入：返回非 null 时，对应变量名的写入（含回滚写入）按错误失败，
    /// 用来稳定复现「A 成功、B 失败」这类真实注册表难以触发的路径。生产环境恒为 null。
    /// </summary>
    internal static Func<string, string?>? WriteFailureInjector { get; set; }

    private static void GuardWriteInjection(string name)
    {
        if (WriteFailureInjector?.Invoke(name) is { } failure)
            throw new InvalidOperationException(failure);
    }

    // ---------- 备份 / 恢复 ----------

    /// <summary>最新一份快照的路径；没有则 null。</summary>
    public static string? LatestBackupPath(EnvScope scope)
    {
        try
        {
            var prefix = ScopeName(scope).ToLowerInvariant() + "-";
            return new DirectoryInfo(BackupsDir)
                .GetFiles(prefix + "*.json")
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                .FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>恢复到最新一份快照。恢复动作本身也会先做一次快照。</summary>
    public static EnvApplyResult RestoreLatest(EnvScope scope)
    {
        var latest = LatestBackupPath(scope);
        if (latest is null) return EnvApplyResult.Fail(EnvFailureCode.RestoreNoBackup);

        EnvVarBackupFile? backup;
        try
        {
            backup = JsonSerializer.Deserialize<EnvVarBackupFile>(File.ReadAllText(latest));
        }
        catch (Exception ex)
        {
            return EnvApplyResult.Fail(EnvFailureCode.RestoreUnreadable, name: latest, detail: ex.Message);
        }

        if (backup is null || backup.Entries.Count == 0)
            return EnvApplyResult.Fail(EnvFailureCode.RestoreEmpty, name: latest);

        // 不支持的注册表类型在 BuildRestoreChanges 里被跳过（它们永远不会被改动，无需还原）；
        // 若快照里全是这类条目，changes 为空 → Apply 直接返回「0 项改动」的成功结果。
        var changes = EnvVarRules.BuildRestoreChanges(backup.Entries);

        return Apply(scope, changes);
    }

    /// <summary>
    /// 写前快照到 <c>backups/</c>，只备份本次真正会被改动的变量名。
    /// 返回（快照对象, 路径, 错误）：失败时错误非 null，调用方<b>必须中止整批写入</b>——没有快照就没有回滚点。
    /// 快照对象同时供写入中途失败时自动回滚使用，避免再读一次文件。
    /// 先写 <c>.tmp</c> 再原子改名：中途失败不会在 backups/ 里留下半个 JSON 污染「恢复上次备份」。
    /// </summary>
    private static (EnvVarBackupFile? Backup, string? Path, string? Error) WriteBackup(
        EnvScope scope, RegistryKey key, IReadOnlyList<EnvVarChange> changes)
    {
        try
        {
            var backup = new EnvVarBackupFile
            {
                Scope = ScopeName(scope),
                TimeUtc = DateTime.UtcNow,
            };

            foreach (var name in changes.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var existing = TryReadEntry(key, name, out var actualName);
                backup.Entries.Add(new EnvVarBackupEntry
                {
                    Name = existing?.Name ?? actualName,
                    Existed = existing is not null,
                    Value = existing?.Value,
                    Kind = existing?.Kind ?? "",
                });
            }

            var path = Path.Combine(
                BackupsDir,
                $"{ScopeName(scope).ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, path, overwrite: true);

            PruneBackups(scope);
            return (backup, path, null);
        }
        catch (Exception ex)
        {
            return (null, null, ex.Message);
        }
    }

    private static void PruneBackups(EnvScope scope)
    {
        try
        {
            var prefix = ScopeName(scope).ToLowerInvariant() + "-";
            var dir = new DirectoryInfo(BackupsDir);

            // 顺手清掉「写 .tmp 之后、改名之前」中断留下的半成品；只认本作用域前缀
            foreach (var temp in dir.GetFiles(prefix + "*.tmp"))
                temp.Delete();

            var files = dir.GetFiles(prefix + "*.json")
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                .ToList();

            for (var i = BackupRetention; i < files.Count; i++)
                files[i].Delete();
        }
        catch
        {
            // 清理失败无所谓，下次扫描还会再试
        }
    }

    // ---------- 广播 ----------

    /// <summary>
    /// 广播 WM_SETTINGCHANGE 通知环境变量已变。这是「尽力通知」：
    /// 只发给顶层窗口、只覆盖当前会话，已运行的进程本来也不会自动更新自己的环境块。
    ///
    /// <b>SendMessageTimeoutW 失败不抛异常</b>（返回 0，错误码在 GetLastError），因此必须显式检查返回值：
    /// 失败只记诊断（<see cref="LastBroadcastDiagnostic"/> + Debug 输出），
    /// 既不算保存失败、也不回滚已经写成功的内容。
    /// </summary>
    public static void NotifyEnvironmentChange()
    {
        LastBroadcastDiagnostic = SendBroadcast();

        if (LastBroadcastDiagnostic is not null)
            Debug.WriteLine($"[EnvVars] {LastBroadcastDiagnostic}");
    }

    /// <summary>最近一次广播失败的原因；成功或还没广播过时为 null。仅诊断用。</summary>
    internal static string? LastBroadcastDiagnostic { get; private set; }

    /// <summary>测试用：强制广播结果（null = 走真实 SendMessageTimeoutW）。</summary>
    internal static bool? BroadcastResultOverride { get; set; }

    private static string? SendBroadcast()
    {
        if (BroadcastResultOverride is { } forced)
            return forced ? null : "广播 WM_SETTINGCHANGE 失败（测试注入的失败结果）";

        try
        {
            var result = SendMessageTimeoutW(
                (IntPtr)HWND_BROADCAST,
                WM_SETTINGCHANGE,
                (IntPtr)OwnBroadcastMarker,
                "Environment",
                SMTO_ABORTIFHUNG,
                3000,
                out _);

            return InterpretBroadcastResult(result, Marshal.GetLastPInvokeError());
        }
        catch (Exception ex)
        {
            return $"广播 WM_SETTINGCHANGE 异常：{ex.Message}";
        }
    }

    /// <summary>
    /// 解释 SendMessageTimeoutW 的原始返回值：非 0 = 至少有一个窗口收到，返回 null；
    /// 0 = 失败/超时，带上 Win32 错误码作为诊断。（抽成纯函数是为了能单测这条判定逻辑。）
    /// </summary>
    internal static string? InterpretBroadcastResult(IntPtr result, int lastError)
        => result != IntPtr.Zero
            ? null
            : $"广播 WM_SETTINGCHANGE 未送达：SendMessageTimeout 返回 0（Win32 错误码 {lastError}）";

    // ---------- 内部工具 ----------

    private static EnvVarEntry ReadEntry(RegistryKey key, string name)
    {
        var kind = key.GetValueKind(name);
        var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

        // REG_SZ / REG_EXPAND_SZ 之外的类型（罕见）也能显示，不至于让整个列表读挂；
        // 但会标记为「不支持」，界面据此禁用编辑，避免一次保存就把它改写成 REG_SZ。
        var kindName = kind.ToString();
        var value = raw as string ?? Convert.ToString(raw) ?? "";

        return new EnvVarEntry
        {
            Name = name,
            Value = value,
            Kind = kindName,
            KindUnsupported = !EnvVarRules.IsSupportedKindName(kindName),
        };
    }

    /// <summary>把值类型名翻译成 RegistryValueKind（决策在 <see cref="EnvVarRules.ResolveValueKindName"/>，只可能落到两种）。</summary>
    private static RegistryValueKind RegistryKindFor(EnvVarChange change)
        => EnvVarRules.ResolveValueKindName(change.ValueKind, change.Value) == "ExpandString"
            ? RegistryValueKind.ExpandString
            : RegistryValueKind.String;

    /// <summary>按名称（大小写不敏感）取回真实条目；<paramref name="actualName"/> 回传注册表里的真实名称。</summary>
    private static EnvVarEntry? TryReadEntry(RegistryKey key, string name, out string actualName)
    {
        actualName = name;

        foreach (var candidate in key.GetValueNames())
        {
            if (!string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) continue;
            actualName = candidate;
            return ReadEntry(key, candidate);
        }

        return null;
    }
}
