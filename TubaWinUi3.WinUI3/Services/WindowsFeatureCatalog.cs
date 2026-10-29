using System.Globalization;

namespace TubaWinUi3.Services;

/// <summary>统一功能目录与批量输入的纯逻辑，不访问系统配置。</summary>
internal static class WindowsFeatureCatalog
{
    public static uint[] ParseIds(string text)
    {
        var parts = text.Split([',', '，'], StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(p => !uint.TryParse(p, NumberStyles.None,
                CultureInfo.InvariantCulture, out var id) || id == 0))
            throw new ArgumentException("请输入有效的功能 ID（1–4294967295），多个 ID 用逗号分隔。请检查空项或非数字内容。");
        return parts.Select(p => uint.Parse(p, CultureInfo.InvariantCulture)).Distinct().ToArray();
    }

    public static List<FeatureFlagEntry> Merge(IEnumerable<FeatureFlagEntry> runtime,
        IEnumerable<FeatureFlagEntry> boot, IReadOnlyDictionary<uint, string> dictionary)
    {
        // 同一 ID 的不同优先级只展示最终优先级最高的配置。
        var current = runtime.GroupBy(e => e.FeatureId).ToDictionary(g => g.Key, g => g.MaxBy(e => e.Priority)!);
        var restart = boot.GroupBy(e => e.FeatureId).ToDictionary(g => g.Key, g => g.MaxBy(e => e.Priority)!);
        return current.Keys.Union(restart.Keys).Union(dictionary.Keys).OrderBy(id => id).Select(id =>
        {
            current.TryGetValue(id, out var r);
            restart.TryGetValue(id, out var b);
            dictionary.TryGetValue(id, out var name);
            var entry = r ?? b ?? new FeatureFlagEntry(id, name, (int)WindowsFeatureService.PriorityUser,
                FeatureState.Default, false, false);
            return entry with { Name = name ?? entry.Name, RuntimeState = r?.State, BootState = b?.State,
                HasConfig = r is not null || b is not null };
        }).ToList();
    }

    public static List<FeatureFlagEntry> Filter(IEnumerable<FeatureFlagEntry> entries, string search, bool namedOnly)
    {
        var term = search.Trim();
        return entries.Where(e => term.Length > 0
            ? e.FeatureId.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.Ordinal)
                || (e.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            : !namedOnly || e.Name is not null).ToList();
    }
}
