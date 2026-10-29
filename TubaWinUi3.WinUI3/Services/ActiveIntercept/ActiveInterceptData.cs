using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace TubaWinUi3.Services.ActiveIntercept;

/// <summary>
/// 主程序读取主动拦截后端落盘数据的辅助类（只读）。
/// 数据目录约定与后端一致：&lt;DataDir&gt;/active_intercept/ 下的
/// events.jsonl（事件）、state.json（期望状态）、ignored.json（停止追踪列表）。
/// </summary>
public static class ActiveInterceptData
{
    public static string DataDir => Path.Combine(ConfigManager.GetDataDir(), "active_intercept");
    public static string EventsPath => Path.Combine(DataDir, "events.jsonl");
    public static string StatePath => Path.Combine(DataDir, "state.json");
    public static string IgnoredPath => Path.Combine(DataDir, "ignored.json");

    /// <summary>读取拦截事件（倒序，最新在前）。文件不存在返回空列表。</summary>
    public static List<InterceptEventDto> ReadEvents()
    {
        var result = new List<InterceptEventDto>();
        try
        {
            if (!File.Exists(EventsPath)) return result;
            foreach (var line in File.ReadLines(EventsPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var evt = JsonSerializer.Deserialize<InterceptEventDto>(line);
                    if (evt is not null)
                    {
                        if (string.IsNullOrWhiteSpace(evt.RowId)) evt.RowId = Guid.NewGuid().ToString("N");
                        result.Add(evt);
                    }
                }
                catch { }
            }
        }
        catch { }
        result.Reverse();
        return result;
    }

    /// <summary>读取新增事件（自指定偏移行数起，正序）。返回新增事件列表 + 更新后的总行数。</summary>
    public static (List<InterceptEventDto> NewEvents, int TotalLines) ReadNewEvents(int previousLineCount)
    {
        var result = new List<InterceptEventDto>();
        int total = previousLineCount;
        try
        {
            if (!File.Exists(EventsPath)) return (result, 0);
            var lines = File.ReadAllLines(EventsPath);
            total = lines.Length;
            for (int i = previousLineCount; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                try
                {
                    var evt = JsonSerializer.Deserialize<InterceptEventDto>(lines[i]);
                    if (evt is not null)
                    {
                        if (string.IsNullOrWhiteSpace(evt.RowId)) evt.RowId = Guid.NewGuid().ToString("N");
                        result.Add(evt);
                    }
                }
                catch { }
            }
        }
        catch { }
        return (result, total);
    }

    /// <summary>读取「停止追踪」列表（ignored.json）。文件不存在返回空列表。</summary>
    public static List<IgnoredItemDto> ReadIgnored()
    {
        var result = new List<IgnoredItemDto>();
        try
        {
            if (!File.Exists(IgnoredPath)) return result;
            var json = File.ReadAllText(IgnoredPath);
            var parsed = JsonSerializer.Deserialize<IgnoredFileDto>(json);
            if (parsed?.Items is not null) return parsed.Items;
        }
        catch { }
        return result;
    }

    /// <summary>读取某程序当前的信任策略（allow/block/ask）。兼容后端以数字或字符串序列化枚举的情况。</summary>
    public static string ReadTrustPolicy(string exePath)
    {
        try
        {
            var path = Path.Combine(DataDir, "trust_policies.json");
            if (!File.Exists(path)) return "ask";
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("Policies", out var arr))
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var ep = item.TryGetProperty("ExePath", out var p) ? p.GetString() : "";
                    if (!string.Equals(ep, exePath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!item.TryGetProperty("Policy", out var pol)) return "ask";
                    if (pol.ValueKind == JsonValueKind.Number)
                    {
                        return pol.GetInt32() switch { 1 => "allow", 2 => "block", _ => "ask" };
                    }
                    var s = pol.GetString() ?? "ask";
                    return s.ToLowerInvariant() switch
                    {
                        "allow" or "1" => "allow",
                        "block" or "2" => "block",
                        _ => "ask",
                    };
                }
            }
        }
        catch { }
        return "ask";
    }

    // ---------- 指令写入（供后端下轮轮询消费） ----------

    private static void WriteCommandFile(object payload)
    {
        try
        {
            var dir = Path.Combine(DataDir, "commands");
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(payload);
            var file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..32] + ".json");
            File.WriteAllText(file, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActiveIntercept] 写指令失败：{ex.Message}");
        }
    }

    /// <summary>放行单条，可同时信任此程序（信任 = 总是放行该程序）。</summary>
    public static void WriteAllowCommand(string id, bool trust)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        WriteCommandFile(new { Action = "allow", Id = id, Trust = trust });
    }

    /// <summary>重新屏蔽单条。</summary>
    public static void WriteReblockCommand(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        WriteCommandFile(new { Action = "reblock", Id = id });
    }

    /// <summary>仅删除事件记录（保留追踪与屏蔽状态，历史清理）。</summary>
    public static void WriteRemoveRowsCommand(IEnumerable<string> rowIds)
    {
        var rows = rowIds?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList() ?? [];
        if (rows.Count == 0) return;
        WriteCommandFile(new { Action = "remove_rows", RowIds = rows });
    }

    /// <summary>停止追踪：删除该条目全部事件记录 + 期望状态，并加入忽略列表（不再拦截、不再提醒）。</summary>
    public static void WriteIgnoreCommand(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        WriteCommandFile(new { Action = "ignore", Id = id });
    }

    /// <summary>恢复追踪：从忽略列表移除，后续出现将重新进入拦截流程。</summary>
    public static void WriteUnignoreCommand(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        WriteCommandFile(new { Action = "unignore", Id = id });
    }

    /// <summary>清空全部事件记录（保留期望状态、信任策略与忽略列表）。</summary>
    public static void WriteClearEventsCommand()
    {
        WriteCommandFile(new { Action = "clear_events" });
    }

    /// <summary>写入信任策略指令（allow/block/ask）。</summary>
    public static void WritePolicyCommand(string exePath, string policy)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return;
        WriteCommandFile(new { Action = "set_policy", Id = "", ExePath = exePath, Policy = policy });
    }

    // 兼容旧调用（仅放行，不信任）
    public static void WriteCommand(string action, string id)
    {
        switch (action)
        {
            case "allow": WriteAllowCommand(id, trust: false); break;
            case "reblock": WriteReblockCommand(id); break;
            case "remove": WriteIgnoreCommand(id); break;
            default: WriteCommandFile(new { Action = action, Id = id }); break;
        }
    }

    /// <summary>批量写入指令（多条指令一次性写入）。</summary>
    public static void WriteBatchCommand(string action, IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id))
                WriteCommand(action, id);
        }
    }
}

/// <summary>后端写入的通知请求 DTO（与后端 NotificationRequest 字段对齐）。</summary>
public sealed class NotificationRequest
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Action { get; set; } = "";
    public string TimestampUtc { get; set; } = "";
}

/// <summary>主程序内部 JSON 源生成上下文（AOT 兼容）。</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(NotificationRequest))]
internal sealed partial class ActiveInterceptJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}

/// <summary>拦截事件 DTO（与后端 InterceptEvent 字段对齐）。</summary>
public sealed class InterceptEventDto : INotifyPropertyChanged
{
    /// <summary>事件行唯一 ID（删除单条记录时使用）。</summary>
    public string RowId { get; set; } = "";

    public string TimestampUtc { get; set; } = "";
    public string Action { get; set; } = "";
    public string Id { get; set; } = "";
    public string SubKey { get; set; } = "";
    public string Clsid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string Source { get; set; } = "";

    /// <summary>是否现代菜单（Windows 11 新右键菜单 / AppX 打包应用扩展）。</summary>
    public bool IsModernMenu { get; set; }

    public string Note { get; set; } = "";

    /// <summary>注册表 hive（0=HKCU，1=HKLM；管道快照字段，文件读取时缺省 0）。</summary>
    public int Hive { get; set; }

    /// <summary>WOW64 视图（0=Default，1=Registry64，2=Registry32）。</summary>
    public int View { get; set; }

    /// <summary>条目类型（0=ShellVerb，1=ShellExtension）。</summary>
    public int Kind { get; set; }

    private bool _selected;

    /// <summary>UI 多选标记（不序列化到 JSON）。变更时通知界面，实现勾选计数实时刷新。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string ActionText => Action switch
    {
        "Blocked" => "已拦截",
        "Reblocked" => "重新拦截",
        "Allowed" => "已放行",
        "Unblocked" => "已解除",
        "BlockedFailed" => "拦截失败",
        "Removed" => "已移除",
        "Restored" => "已撤销恢复",
        "Reappeared" => "重现拦截",
        "Ignored" => "停止追踪",
        "Tracking" => "恢复追踪",
        "Pending" => "待审核",
        "Purged" => "永久清除",
        "Modified" => "外部修改",
        _ => Action,
    };

    /// <summary>截断版注册表位置（用于列表显示）。</summary>
    public string SubKeyShort
    {
        get
        {
            if (string.IsNullOrEmpty(SubKey)) return "";
            return SubKey.Length <= 60 ? SubKey : "…" + SubKey[^60..];
        }
    }

    /// <summary>所属程序文件名（列表显示用）。</summary>
    public string ExeFileName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ExePath)) return "";
            try { return System.IO.Path.GetFileName(ExePath); } catch { return ExePath; }
        }
    }

    /// <summary>本地时间显示。</summary>
    public string TimeText
    {
        get
        {
            if (DateTime.TryParse(TimestampUtc, null, System.Globalization.DateTimeStyles.RoundtripKind,
                    out var dt))
            {
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }
            return TimestampUtc;
        }
    }

    /// <summary>就地同步新快照数据（保留本实例勾选状态；触发全部显示属性刷新）。</summary>
    public void CopyFrom(InterceptEventDto other)
    {
        RowId = other.RowId;
        TimestampUtc = other.TimestampUtc;
        Action = other.Action;
        Id = other.Id;
        SubKey = other.SubKey;
        Clsid = other.Clsid;
        Name = other.Name;
        Command = other.Command;
        ExePath = other.ExePath;
        Source = other.Source;
        IsModernMenu = other.IsModernMenu;
        Note = other.Note;
        Hive = other.Hive;
        View = other.View;
        Kind = other.Kind;
        OnPropertyChanged("");
    }
}

/// <summary>「停止追踪」列表 DTO（与后端 IgnoreEntry 字段对齐）。</summary>
public sealed class IgnoredItemDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SubKey { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string CreatedUtc { get; set; } = "";
    public string Note { get; set; } = "";

    public string TimeText
    {
        get
        {
            if (DateTime.TryParse(CreatedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            return CreatedUtc;
        }
    }
}

/// <summary>忽略列表文件 DTO。</summary>
public sealed class IgnoredFileDto
{
    public List<IgnoredItemDto> Items { get; set; } = [];
}