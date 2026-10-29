using System.Text.Json;

namespace TubaWinUi3.Services.HandleCleaner;

/// <summary>
/// 「句柄清理」的持久化状态：只记上次安全清理的摘要（时间 / 释放数 / 涉及进程数），
/// 落盘在 <c>&lt;DataDir&gt;\HandleCleaner\state.json</c>。读写失败一律静默退回默认值。
/// </summary>
public sealed class HandleCleanerState
{
    /// <summary>上次安全清理完成时间（UTC）。</summary>
    public DateTime? LastCleanUtc { get; set; }

    /// <summary>上次安全清理实际释放的句柄数。</summary>
    public int LastFreedHandles { get; set; }

    /// <summary>上次安全清理涉及的进程数。</summary>
    public int LastProcessCount { get; set; }

    /// <summary>测试注入用：替换数据目录。</summary>
    internal static string? DataDirOverride { get; set; }

    internal static string DataDir
    {
        get
        {
            var dir = DataDirOverride ?? Path.Combine(ConfigManager.GetDataDir(), "HandleCleaner");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string StatePath => Path.Combine(DataDir, "state.json");

    public static HandleCleanerState Load()
    {
        try
        {
            if (File.Exists(StatePath)) return Parse(File.ReadAllText(StatePath));
        }
        catch
        {
            // 状态损坏不影响本次使用
        }
        return new HandleCleanerState();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(StatePath, Serialize());
        }
        catch
        {
            // 落盘失败只是丢一条历史记录，不影响清理本身
        }
    }

    internal static HandleCleanerState Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<HandleCleanerState>(json) ?? new HandleCleanerState();
        }
        catch (JsonException)
        {
            // 内容损坏（截断/伪 JSON）按「没有记录」处理，不影响本次使用。
            return new HandleCleanerState();
        }
    }

    internal string Serialize()
        => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}
