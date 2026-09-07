using System.Text.Json;

namespace TubaWinUi3.Services;

public static class FavoritesService
{
    private const string Key = "FavoriteTools";
    private static string FavoritesPath => ConfigManager.GetFavoritesPath();
    private static List<string>? _cache;

    public static IReadOnlyList<string> GetFavorites()
    {
        if (_cache is not null)
            return _cache;

        try
        {
            if (File.Exists(FavoritesPath))
            {
                var json = File.ReadAllText(FavoritesPath);
                var stored = JsonSerializer.Deserialize<List<string>>(json) ?? [];
                var resolved = stored.Select(p => PathResolver.MakeAbsolute(p)).ToList();
                _cache = RepairBrokenFavorites(resolved);
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

    /// <summary>
    /// P1-7 自愈：工具包重下 / 应用目录迁移后 {ToolsRoot} 基址漂移，
    /// 旧收藏路径会指向不存在的位置，表现为"收藏悄悄清空"。
    /// 这里按尾部路径特征（最多 4 段）在候选工具根目录下重新锚定，
    /// 有修复即回写收藏文件。找不到的保持原样（工具被卸载属正常失效）。
    /// </summary>
    private static List<string> RepairBrokenFavorites(List<string> paths)
    {
        var roots = CandidateToolRoots().ToList();
        if (roots.Count == 0)
            return paths;

        List<string>? result = null;
        for (int i = 0; i < paths.Count; i++)
        {
            var p = paths[i];
            var finalPath = p;
            if (!string.IsNullOrWhiteSpace(p) && !File.Exists(p) && !Directory.Exists(p))
                finalPath = TryReanchor(p, roots) ?? p;

            if (!ReferenceEquals(finalPath, p) && result is null)
                result = new List<string>(paths);
            if (result is not null)
                result[i] = finalPath;
        }

        if (result is not null)
            Save(result);
        return result ?? paths;
    }

    private static string? TryReanchor(string brokenPath, IReadOnlyList<string> roots)
    {
        try
        {
            var segs = brokenPath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length == 0)
                return null;

            for (int take = Math.Min(segs.Length, 4); take >= 1; take--)
            {
                var sub = string.Join(Path.DirectorySeparatorChar.ToString(), segs[^take..]);
                foreach (var root in roots)
                {
                    if (string.IsNullOrWhiteSpace(root))
                        continue;
                    var candidate = Path.Combine(root, sub);
                    if (File.Exists(candidate) || Directory.Exists(candidate))
                        return candidate;
                }
            }
        }
        catch
        {
            // 自愈失败不抛出，保持原路径
        }
        return null;
    }

    private static string? TryGet(Func<string> resolver)
    {
        try
        {
            return resolver();
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> CandidateToolRoots()
    {
        var toolsRoot = TryGet(() => ToolCatalog.ToolsRoot);
        if (!string.IsNullOrWhiteSpace(toolsRoot))
            yield return toolsRoot;
        var appDataTools = TryGet(() => Path.Combine(PathResolver.ExpandPath("{AppDataDir}"), "Tools"));
        if (!string.IsNullOrWhiteSpace(appDataTools))
            yield return appDataTools;
    }

    public static bool IsFavorite(string toolPath)
    {
        return GetFavorites().Contains(toolPath, StringComparer.OrdinalIgnoreCase);
    }

    public static void AddFavorite(string toolPath)
    {
        var list = GetFavorites()
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        if (list.Contains(toolPath, StringComparer.OrdinalIgnoreCase))
            return;

        list.Add(toolPath);
        _cache = list;
        Save(list);
    }

    public static void RemoveFavorite(string toolPath)
    {
        var list = GetFavorites()
            .Where(p => !p.Equals(toolPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _cache = list;
        Save(list);
    }

    public static void RemoveAll()
    {
        _cache = [];
        Save([]);
    }

    public static void ToggleFavorite(string toolPath)
    {
        if (IsFavorite(toolPath))
            RemoveFavorite(toolPath);
        else
            AddFavorite(toolPath);
    }

    /// <summary>按给定顺序保存收藏(拖拽排序后调用)。</summary>
    public static void SaveOrder(IEnumerable<string> orderedPaths)
    {
        var list = orderedPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        _cache = list;
        Save(list);
    }

    public static void InvalidateCache()
    {
        _cache = null;
    }

    private static void Save(List<string> favorites)
    {
        try
        {
            var dir = Path.GetDirectoryName(FavoritesPath)!;
            Directory.CreateDirectory(dir);
            var stored = favorites.Select(p => PathResolver.MakeRelative(p)).ToList();
            var json = JsonSerializer.Serialize(stored);
            File.WriteAllText(FavoritesPath, json);
        }
        catch { }
    }
}
