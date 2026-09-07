using System.Text.Json.Serialization;

namespace TubaWinUi3.Models;

/// <summary>
/// 正版软件商店 — 分类
/// </summary>
public sealed class StoreCategory
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("glyph")]
    public string Glyph { get; init; } = "";

    [JsonPropertyName("packages")]
    public List<StorePackage> Packages { get; init; } = [];

    [JsonPropertyName("subCategories")]
    public List<StoreSubCategory>? SubCategories { get; init; }
}

/// <summary>
/// 正版软件商店 — 子分类
/// </summary>
public sealed class StoreSubCategory
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("packages")]
    public List<StorePackage> Packages { get; init; } = [];
}

/// <summary>
/// 正版软件商店 — 软件包
/// </summary>
public sealed class StorePackage
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("desc")]
    public string? Description { get; init; }

    [JsonPropertyName("recommended")]
    public bool IsRecommended { get; init; }

    // ---- 以下为运行时附加字段，不参与 JSON 反序列化 ----

    [JsonIgnore]
    public string Category { get; set; } = "";

    [JsonIgnore]
    public string Glyph { get; set; } = "";

    [JsonIgnore]
    public bool IsOnlineResult { get; set; }

    /// <summary>安装状态：idle / resolving / queued / done / error</summary>
    [JsonIgnore]
    public string InstallState { get; set; } = "idle";

    [JsonIgnore]
    public string? StatusText { get; set; }
}

/// <summary>
/// pcsetup_catalog.json 顶层结构
/// </summary>
public sealed class PcSetupCatalog
{
    [JsonPropertyName("categories")]
    public List<StoreCategory> Categories { get; init; } = [];
}

/// <summary>
/// WinGet REST API 搜索结果项
/// </summary>
public sealed class WingetSearchResult
{
    public string PackageIdentifier { get; init; } = "";
    public string PackageName { get; init; } = "";
    public string? Publisher { get; init; }
    public string? Description { get; init; }
    public string? LatestVersion { get; init; }
}
