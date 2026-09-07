using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class UnifiedSearchService
{
    private static readonly (string Title, string Subtitle, string Glyph, string SettingKey)[] SettingsEntries =
    [
        ("应用主题", "选择浅色、深色或跟随系统", "\uE790", "Theme"),
        ("简洁列表模式", "原版图吧工具箱的样式", "\uE8FD", "CompactMode"),
        ("显示品牌 Logo", "在硬件信息页面显示品牌图标", "\uE8F1", "BrandLogo"),
        ("默认启动页面", "选择应用启动后默认打开的页面", "\uE8A5", "DefaultPage"),
        ("快速模式", "禁用所有动画，提升响应速度", "\uEB3F", "FastMode"),
        ("截图水印", "硬件信息截图时添加水印", "\uE8B9", "Watermark"),
        ("记住窗口位置和大小", "关闭后下次启动恢复位置", "\uE784", "RememberWindow"),
        ("背景图片", "导入图片作为主页面背景", "\uE91B", "Background"),
        ("检查更新", "检查是否有新版本", "\uE895", "Update"),
        ("配置管理", "管理配置文件的存储位置、导出和导入", "\uE8B7", "ConfigManager"),
        ("自定义工具管理", "管理工具分类、导入自定义工具", "\uE8B7", "CustomToolManager"),
        ("导出当前软件", "打包成可分发压缩包", "\uE896", "ExportApp"),
    ];

    private static readonly (string Title, string Subtitle, string Glyph, string Action)[] QuickActions =
    [
        ("硬件信息", "查看处理器、显卡、内存等硬件信息", "\uE977", "navigate:hardware"),
        ("常用工具", "查看收藏的工具", "\uE735", "navigate:favorites"),
        ("内置工具", "无需外部文件的系统工具", "\uE90F", "navigate:builtin"),
		("设置", "应用外观和功能设置", "\uE713", "navigate:settings"),
    ];

    public static IReadOnlyList<SearchResult> Search(string query)
    {
        var normalized = query.Trim();
        if (normalized.Length == 0)
            return [];

        var results = new List<SearchResult>();

        SearchExternalTools(normalized, results);
        SearchBuiltinTools(normalized, results);
        if (!RuntimeHelper.IsMsixPackaged)
            SearchCommunityTools(normalized, results);
        SearchSettings(normalized, results);

        DeduplicateResults(results);

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(20)
            .ToList();
    }

    public static IReadOnlyList<SearchResult> GetQuickPanelItems()
    {
        var items = new List<SearchResult>();

        var recentPaths = LaunchHistoryService.GetHistory();
        foreach (var toolPath in recentPaths.Take(3))
        {
            var tool = FindToolByPath(toolPath);
            if (tool is not null)
            {
                var iconPath = tool.IconPath ?? ToolIconService.GetCachedIconPath(tool.Path);
                items.Add(new SearchResult
                {
                    Title = tool.Name,
                    Subtitle = tool.Category,
                    Glyph = tool.IconGlyph ?? "\uE8B7",
                    Kind = tool.DatabaseSource?.Equals("custom", StringComparison.OrdinalIgnoreCase) == true
                        ? SearchItemKind.CustomTool
                        : SearchItemKind.ExternalTool,
                    MatchKey = tool.Path,
                    IconPath = iconPath,
                    Category = tool.Category,
                    Score = 100
                });
            }
        }

        foreach (var qa in QuickActions)
        {
            items.Add(new SearchResult
            {
                Title = qa.Title,
                Subtitle = qa.Subtitle,
                Glyph = qa.Glyph,
                Kind = SearchItemKind.QuickAction,
                MatchKey = qa.Action,
                Score = 50
            });
        }

        return items;
    }

    private static void SearchExternalTools(string query, List<SearchResult> results)
    {
        try
        {
            var tools = ToolCatalog.Search(query);
            foreach (var tool in tools)
            {
                var isCustom = tool.DatabaseSource?.Equals("custom", StringComparison.OrdinalIgnoreCase) == true;
                var score = CalcScore(query, tool.Name, tool.Tags);
                var iconPath = tool.IconPath ?? ToolIconService.GetCachedIconPath(tool.Path);
                results.Add(new SearchResult
                {
                    Title = tool.Name,
                    Subtitle = isCustom ? $"自定义 · {tool.Category}" : tool.Category,
                    Glyph = tool.IconGlyph ?? "\uE8B7",
                    Kind = isCustom ? SearchItemKind.CustomTool : SearchItemKind.ExternalTool,
                    MatchKey = tool.Path,
                    IconPath = iconPath,
                    Category = tool.Category,
                    Score = isCustom ? score + 1 : score
                });
            }
        }
        catch { }
    }

    private static void SearchBuiltinTools(string query, List<SearchResult> results)
    {
        try
        {
            foreach (var tool in BuiltinToolRegistry.Tools)
            {
                var score = CalcScore(query, tool.Name, [tool.Description, tool.Category]);
                if (score > 0)
                {
                    results.Add(new SearchResult
                    {
                        Title = tool.Name,
                        Subtitle = tool.Category,
                        Glyph = tool.Glyph,
                        Kind = SearchItemKind.BuiltinTool,
                        MatchKey = tool.Id,
                        Category = tool.Category,
                        Score = score
                    });
                }
            }
        }
        catch { }
    }

    private static void SearchSettings(string query, List<SearchResult> results)
    {
        foreach (var s in SettingsEntries)
        {
            var score = CalcScore(query, s.Title, [s.Subtitle]);
            if (score > 0)
            {
                results.Add(new SearchResult
                {
                    Title = s.Title,
                    Subtitle = s.Subtitle,
                    Glyph = s.Glyph,
                    Kind = SearchItemKind.Setting,
                    MatchKey = s.SettingKey,
                    Score = score
                });
            }
        }
    }

    private static void SearchCommunityTools(string query, List<SearchResult> results)
    {
    }

    private static void DeduplicateResults(List<SearchResult> results)
    {
        var builtinIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in results)
        {
            if (r.Kind == SearchItemKind.ExternalTool)
            {
                var tool = ToolCatalog.GetAllToolsCached()
                    .FirstOrDefault(t => t.Path.Equals(r.MatchKey, StringComparison.OrdinalIgnoreCase));
                if (tool?.IsBuiltinLink == true && !string.IsNullOrWhiteSpace(tool.BuiltinToolId))
                    builtinIds.Add(tool.BuiltinToolId);
            }
        }

        var seen = new HashSet<(SearchItemKind, string)>();
        for (var i = results.Count - 1; i >= 0; i--)
        {
            var r = results[i];
            if (r.Kind == SearchItemKind.BuiltinTool && builtinIds.Contains(r.MatchKey))
            {
                results.RemoveAt(i);
                continue;
            }
            var key = (r.Kind, r.MatchKey);
            if (!seen.Add(key))
                results.RemoveAt(i);
        }
    }

    private static ToolItem? FindToolByPath(string toolPath)
    {
        try
        {
            return ToolCatalog.GetAllToolsCached()
                .FirstOrDefault(t => t.Path.Equals(toolPath, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    internal static double CalcScore(string query, string primary, IReadOnlyList<string>? secondary)
    {
        var score = 0.0;

        if (primary.Equals(query, StringComparison.CurrentCultureIgnoreCase))
            score += 100;
        else if (primary.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
            score += 80;
        else if (primary.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            score += 60;

        if (secondary is not null)
        {
            foreach (var text in secondary)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    score += 20;
            }
        }

        return score;
    }
}
