namespace TubaWinUi3.Services.MemoryManagement;

public static class PageFileMigrationPlanner
{
    public static IReadOnlyList<PageFileEntry> EffectiveEntries(PageFileState state)
    {
        var entries = state.Configuration.Entries.Where(e => e.Mode != PageFileMode.None).ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var usage in state.Usage) entries.TryAdd(usage.Path, new(usage.Path, PageFileMode.SystemManaged, 0, 0));
        return entries.Values.ToList();
    }
    public static PageFileConfiguration Build(PageFileState state, string source, string destinationRoot, string systemRoot, bool keepSystem, PageFileMode mode, uint initial, uint maximum)
    {
        if (string.Equals(Path.GetPathRoot(source), destinationRoot, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("当前位置与目标磁盘不能相同。");
        var entries = EffectiveEntries(state).ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
        if (!entries.Remove(source)) throw new ArgumentException("找不到原分页文件，请刷新配置。");
        string destination = Path.Combine(destinationRoot, "pagefile.sys");
        if (mode is not (PageFileMode.SystemManaged or PageFileMode.Custom)) throw new ArgumentException("请选择系统管理或自定义大小。");
        entries[destination] = new(destination, mode, mode == PageFileMode.Custom ? initial : 0, mode == PageFileMode.Custom ? maximum : 0);
        string systemPath = Path.Combine(systemRoot, "pagefile.sys");
        if (keepSystem && !destination.Equals(systemPath, StringComparison.OrdinalIgnoreCase)) entries.TryAdd(systemPath, new(systemPath, PageFileMode.SystemManaged, 0, 0));
        return new(false, entries.Values.OrderBy(e => e.Path).ToList());
    }
}
