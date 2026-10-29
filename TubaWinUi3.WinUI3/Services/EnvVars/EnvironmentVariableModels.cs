using System.ComponentModel;

namespace TubaWinUi3.Services.EnvVars;

/// <summary>环境变量作用域：用户（HKCU\Environment）或系统（HKLM\...\Session Manager\Environment）。</summary>
public enum EnvScope
{
    User = 0,
    System = 1,
}

/// <summary>
/// 一个环境变量条目。值保持注册表里的原样（<c>%VAR%</c> 不展开）——
/// 展开只发生在显示给用户看的时候，写回必须原样。
/// 实现变更通知：界面双向绑定与「脏状态」判定都依赖它。
/// </summary>
public sealed class EnvVarEntry : INotifyPropertyChanged
{
    private string _name = "";
    private string _value = "";
    private string _kind = "";

    public string Name
    {
        get => _name;
        set => Set(ref _name, value, nameof(Name));
    }

    /// <summary>注册表里存的原样值（REG_EXPAND_SZ 也不展开）。</summary>
    public string Value
    {
        get => _value;
        set => Set(ref _value, value, nameof(Value));
    }

    /// <summary>注册表值类型名（String / ExpandString / 其它），展示用。</summary>
    public string Kind
    {
        get => _kind;
        set => Set(ref _kind, value, nameof(Kind));
    }

    /// <summary>
    /// 该变量的注册表类型不在本工具支持范围（非 REG_SZ / REG_EXPAND_SZ）。
    /// 这类值读出来只是字符串化的展示文本，写回去必然发生类型转换，界面据此禁用编辑与删除。
    /// </summary>
    public bool KindUnsupported { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public EnvVarEntry Clone() => new() { Name = Name, Value = Value, Kind = Kind, KindUnsupported = KindUnsupported };

    private void Set(ref string field, string value, string propertyName)
    {
        if (string.Equals(field, value, StringComparison.Ordinal)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>一次待应用的改动（<see cref="Value"/> 为 null 表示删除该变量）。</summary>
public enum EnvChangeKind
{
    Set,
    Delete,
}

/// <summary>
/// 失败原因代码。服务层只产出代码与参数，界面按代码查资源键渲染文案（zh-CN / en-US）；
/// 服务层不再产出任何用户可见文本，原始异常文本只进 <c>Detail</c> 供日志。
/// </summary>
public enum EnvFailureCode
{
    None = 0,

    // ---- 校验类（FailureName = 变量名）----
    NameEmpty,
    NameWhitespace,
    NameEqualsSign,
    NameControlChar,
    NameTooLong,
    ValueNullChar,
    TotalTooLong,

    // ---- 读取 / 打开 ----
    ReadFailed,
    KeyUnavailable,   // FailureName = 注册表项路径

    // ---- 写入 ----
    AccessDenied,
    BackupFailed,     // Detail = 原始异常
    WriteFailed,      // Rollback 带回滚结果
    VerifyFailed,     // 同上
    DeleteFailed,     // 同上
    UnsupportedKind,  // FailureName = 变量名

    // ---- 恢复 ----
    RestoreNoBackup,
    RestoreUnreadable,   // Detail = 原始异常
    RestoreEmpty,
}

/// <summary>写入失败后自动回滚的结果。</summary>
public enum EnvRollbackState
{
    NotAttempted = 0,
    Succeeded,
    Failed,
}

/// <summary>
/// 一次待应用的改动。
/// <para>
/// <see cref="ValueKind"/>：写入时用的注册表值类型名（"String" / "ExpandString"）。
/// null 表示未指定——按「值里有没有 %」的启发式推断（用户的普通编辑走这条）；
/// 恢复备份时会原样带上快照里的 Kind，不能再靠推断，否则「无 % 的 REG_EXPAND_SZ」会被降级成 REG_SZ。
/// </para>
/// </summary>
public sealed record EnvVarChange(string Name, EnvChangeKind Kind, string? Value, string? ValueKind = null);

/// <summary>写前快照的一条：区分「原来就没有」与「原来有值」，恢复时才能各归各位。</summary>
public sealed class EnvVarBackupEntry
{
    public string Name { get; set; } = "";

    /// <summary>写入前该变量是否已存在。</summary>
    public bool Existed { get; set; }

    /// <summary>写入前的原样值（存在时）。</summary>
    public string? Value { get; set; }

    /// <summary>写入前的注册表值类型名。</summary>
    public string Kind { get; set; } = "";
}

/// <summary>一份写前快照（落盘为 JSON，供「恢复上次备份」使用）。</summary>
public sealed class EnvVarBackupFile
{
    public string Scope { get; set; } = "";

    public DateTime TimeUtc { get; set; }

    public List<EnvVarBackupEntry> Entries { get; set; } = [];
}

/// <summary>
/// 环境变量的纯规则：校验、值类型判定、列表型变量判定、PATH 拆合与去重归一化。
/// 不碰注册表、不碰 UI，便于单测。
/// </summary>
internal static class EnvVarRules
{
    /// <summary>变量名长度上限（与 PowerToys 一致）。</summary>
    internal const int MaxNameLength = 259;

    /// <summary><c>名=值</c> 总长度上限。</summary>
    internal const int MaxTotalLength = 32766;

    /// <summary>
    /// 按分号分段的列表型变量。只用于决定界面上要不要给「逐条编辑」，
    /// 不参与读写口径。
    /// </summary>
    private static readonly string[] ListVariableNames =
    [
        "PATH",
        "PATHEXT",
        "PSMODULEPATH",
        "_NT_SYMBOL_PATH",
        "_NT_ALT_SYMBOL_PATH",
        "_NT_SYMCACHE_PATH",
    ];

    internal static bool IsListVariable(string name)
        => ListVariableNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 值里出现 <c>%</c> 就存 REG_EXPAND_SZ（PowerToys 同款启发式）。
    /// 单独一个 <c>%</c> 也会命中——这是刻意的取舍，宁可多展开也不要让 <c>%VAR%</c> 变成死字符串。
    /// </summary>
    internal static bool ShouldStoreAsExpandString(string? value)
        => value is not null && value.Contains('%');

    /// <summary>
    /// 决定写入时用的注册表值类型名（"String" / "ExpandString"）。
    /// 显式指定优先（恢复备份：快照里的 Kind 原样写回，即使值里没有 <c>%</c> 也要还原成 REG_EXPAND_SZ，
    /// 反之有 <c>%</c> 的 REG_SZ 也不能被改成 ExpandString）；未指定才用「含 % → ExpandString」的启发式。
    /// 只认识这两种（大小写容错，兜手工改过的备份）：MultiString / DWord 等罕见类型按字符串读出来后
    /// 无法按原类型写回，退回启发式。
    /// </summary>
    internal static string ResolveValueKindName(string? explicitKind, string? value)
    {
        if (string.Equals(explicitKind, "String", StringComparison.OrdinalIgnoreCase)) return "String";
        if (string.Equals(explicitKind, "ExpandString", StringComparison.OrdinalIgnoreCase)) return "ExpandString";

        return ShouldStoreAsExpandString(value) ? "ExpandString" : "String";
    }

    /// <summary>
    /// 本工具只支持 REG_SZ / REG_EXPAND_SZ 两种值类型（环境块本来就只消费这两种）；
    /// 空串视为「界面上新建、还没落过盘的条目」。
    /// </summary>
    internal static bool IsSupportedKindName(string? kind)
        => string.IsNullOrEmpty(kind)
           || string.Equals(kind, "String", StringComparison.OrdinalIgnoreCase)
           || string.Equals(kind, "ExpandString", StringComparison.OrdinalIgnoreCase);

    /// <summary>校验变量名，合法返回 null，否则返回失败代码（文案由界面层渲染）。</summary>
    internal static EnvFailureCode? ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name)) return EnvFailureCode.NameEmpty;
        if (name != name.Trim()) return EnvFailureCode.NameWhitespace;
        if (name.Contains('=')) return EnvFailureCode.NameEqualsSign;
        if (name.Any(char.IsControl)) return EnvFailureCode.NameControlChar;
        if (name.Length > MaxNameLength) return EnvFailureCode.NameTooLong;
        return null;
    }

    /// <summary>校验变量值，合法返回 null，否则返回失败代码。</summary>
    internal static EnvFailureCode? ValidateValue(string value)
        => value.Contains('\0') ? EnvFailureCode.ValueNullChar : null;

    /// <summary>校验「名=值」总长度，合法返回 null，否则返回失败代码。</summary>
    internal static EnvFailureCode? ValidateTotalLength(string name, string value)
        => name.Length + 1 + value.Length > MaxTotalLength ? EnvFailureCode.TotalTooLong : null;

    /// <summary>
    /// 按分号拆成条目。<b>空项保留</b>（PowerToys 允许空项，用户可能正靠它占位），
    /// 因此 <c>"a;b;;c"</c> 拆出 4 条，join 回去与原文完全相等。
    /// </summary>
    internal static List<string> SplitList(string value) => [.. value.Split(';')];

    internal static string JoinList(IEnumerable<string> entries) => string.Join(';', entries);

    /// <summary>
    /// 去重比较用的归一化键：展开 <c>%VAR%</c> → 统一分隔符 → 去掉结尾分隔符。
    /// <b>只用于比较</b>，写回的一律是用户原来的条目文本。
    /// </summary>
    internal static string NormalizePathEntry(string entry)
    {
        var text = entry.Trim();
        if (text.Length == 0) return "";

        text = Environment.ExpandEnvironmentVariables(text);
        text = text.Replace('/', '\\');
        return Path.TrimEndingDirectorySeparator(text);
    }

    /// <summary>
    /// 列表型变量的条目去重：保持首次出现的顺序，大小写不敏感。
    /// 空项不参与去重（保留原样），否则 <c>"a;;a"</c> 会被压成一条、丢掉占位。
    /// </summary>
    internal static List<string> DedupeList(IReadOnlyList<string> entries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(entries.Count);

        foreach (var entry in entries)
        {
            var key = NormalizePathEntry(entry);
            if (key.Length == 0)
            {
                result.Add(entry);
                continue;
            }

            if (seen.Add(key)) result.Add(entry);
        }

        return result;
    }

    /// <summary>
    /// 比较两份变量表，得出实际需要写注册表的改动。
    /// 改动是按「名」对齐的：改名等价于「删旧 + 建新」，值不同才是改值。
    /// 只有被改动的变量会被写入——没动过的条目一个字节都不碰。
    /// </summary>
    internal static List<EnvVarChange> BuildChanges(
        IReadOnlyList<EnvVarEntry> original,
        IReadOnlyList<EnvVarEntry> working)
    {
        var changes = new List<EnvVarChange>();

        var originalByName = new Dictionary<string, EnvVarEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in original)
            originalByName[entry.Name] = entry;

        var workingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in working)
        {
            workingNames.Add(entry.Name);

            if (!originalByName.TryGetValue(entry.Name, out var before))
            {
                changes.Add(new EnvVarChange(entry.Name, EnvChangeKind.Set, entry.Value));
            }
            else if (!string.Equals(before.Value, entry.Value, StringComparison.Ordinal))
            {
                changes.Add(new EnvVarChange(entry.Name, EnvChangeKind.Set, entry.Value));
            }
        }

        foreach (var entry in original)
        {
            if (!workingNames.Contains(entry.Name))
                changes.Add(new EnvVarChange(entry.Name, EnvChangeKind.Delete, null));
        }

        return changes;
    }

    /// <summary>
    /// 备份条目 → 改动列表（「恢复上次备份」用）。
    /// 恢复必须原样还原写入前的内容，包括值类型：Kind 要一路带进 <see cref="EnvVarChange"/>。
    /// 不支持的注册表类型（非 REG_SZ / REG_EXPAND_SZ）直接跳过：Apply 侧对这类变量有硬门禁、
    /// 永远不会改动它们，快照里存的又只是字符串化后的展示文本，写回去只会造成类型转换。
    /// </summary>
    internal static List<EnvVarChange> BuildRestoreChanges(IReadOnlyList<EnvVarBackupEntry> entries)
    {
        var changes = new List<EnvVarChange>(entries.Count);

        foreach (var entry in entries)
        {
            if (entry.Existed && !IsSupportedKindName(entry.Kind)) continue;

            changes.Add(entry.Existed
                ? new EnvVarChange(entry.Name, EnvChangeKind.Set, entry.Value ?? "", entry.Kind)
                : new EnvVarChange(entry.Name, EnvChangeKind.Delete, null));
        }

        return changes;
    }
}
