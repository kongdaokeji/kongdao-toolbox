using System.Text.Json;

namespace TubaWinUi3.Services;

public static class AppSettings
{
    public static event Action<string>? SettingChanged;

    private static string SettingsPath => ConfigManager.GetSettingsPath();

    private static readonly object _gate = new();
    private static Dictionary<string, string>? _cache;
    private static bool _dirty;

    // 写盘去抖：连续 Set 合并为一次落盘，拖拽排序等高频调用不再每次都
    // 全量序列化 + 同步写文件；Save 在后台线程执行，不阻塞调用方。
    private static readonly System.Threading.Timer _persistTimer =
        new(_ => Save(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

    private const int PersistDebounceMs = 500;

    public static Dictionary<string, string> Load()
    {
        lock (_gate)
        {
            if (_cache is not null) return _cache;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
                }
                else
                {
                    _cache = [];
                }
            }
            catch
            {
                _cache = [];
            }
            return _cache;
        }
    }

    public static void Save()
    {
        lock (_gate)
        {
            if (!_dirty || _cache is null) return;
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_cache);
                File.WriteAllText(SettingsPath, json);
                _dirty = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] Save failed: {ex.Message}");
            }
        }
    }

    /// <summary>立即同步落盘（窗口关闭/退出前调用，避免去抖窗口内丢失最后一次变更）。</summary>
    public static void Flush() => Save();

    private static void SchedulePersist()
    {
        try { _persistTimer.Change(PersistDebounceMs, System.Threading.Timeout.Infinite); }
        catch { }
    }

    // 事件在锁外广播，逐订阅者隔离异常，单个处理器失败不中断其余订阅者。
    private static void RaiseSettingChanged(string key)
    {
        var handler = SettingChanged;
        if (handler is null) return;
        foreach (var subscriber in handler.GetInvocationList())
        {
            try { ((Action<string>)subscriber)(key); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] SettingChanged subscriber error: {ex.Message}");
            }
        }
    }

    public static void Set(string key, string value)
    {
        lock (_gate)
        {
            var s = Load();
            s[key] = value;
            _dirty = true;
            SchedulePersist();
        }
        RaiseSettingChanged(key);
    }

    public static void Set(string key, bool value) => Set(key, value.ToString().ToLowerInvariant());
    public static void Set(string key, int value) => Set(key, value.ToString());
    public static void Set(string key, double value) => Set(key, value.ToString("F2"));

    public static void Remove(string key)
    {
        lock (_gate)
        {
            var s = Load();
            s.Remove(key);
            _dirty = true;
            SchedulePersist();
        }
        RaiseSettingChanged(key);
    }

    public static string? Get(string key)
    {
        var s = Load();
        return s.TryGetValue(key, out var v) ? v : null;
    }

    public static bool GetBool(string key, bool defaultValue = false)
    {
        var v = Get(key);
        return v is not null && bool.TryParse(v, out var b) ? b : defaultValue;
    }

    public static int GetInt(string key, int defaultValue = 0)
    {
        var v = Get(key);
        return v is not null && int.TryParse(v, out var i) ? i : defaultValue;
    }

    public static double GetDouble(string key, double defaultValue = 0)
    {
        var v = Get(key);
        return v is not null && double.TryParse(v, out var d) ? d : defaultValue;
    }

    public static void InvalidateCache()
    {
        lock (_gate)
        {
            _cache = null;
            _dirty = false;
        }
    }
}