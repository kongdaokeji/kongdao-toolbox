namespace TubaWinUi3.Models;

public sealed class SearchResult
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Glyph { get; init; }
    public required SearchItemKind Kind { get; init; }
    public required string MatchKey { get; init; }
    public string? IconPath { get; init; }
    public string? Category { get; init; }
    public double Score { get; init; }

    public bool HasIconPath => !string.IsNullOrEmpty(IconPath);

    public string KindText => Kind switch
    {
        SearchItemKind.ExternalTool => "工具",
        SearchItemKind.BuiltinTool => "内置",
        SearchItemKind.Setting => "设置",
        SearchItemKind.CustomTool => "自定义",
        SearchItemKind.QuickAction => "快捷",
        SearchItemKind.CommunityTool => "社区",
        _ => ""
    };

    public override string ToString() => Title;
}

public enum SearchItemKind
{
    ExternalTool,
    BuiltinTool,
    Setting,
    CustomTool,
    QuickAction,
    CommunityTool
}

public sealed class SearchNavigationTarget
{
    public string? HighlightToolPath { get; init; }
    public string? HighlightSettingKey { get; init; }
    public string? HighlightBuiltinId { get; init; }
}
