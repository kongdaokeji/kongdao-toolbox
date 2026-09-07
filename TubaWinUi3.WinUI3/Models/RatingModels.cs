using System.Text.Json.Serialization;

namespace TubaWinUi3.Models;

/// <summary> 笔记本排行榜条目（机型聚合） </summary>
public sealed class LaptopLeaderboardEntry
{
	public string DeviceModel { get; set; } = "";
	public string Cpu { get; set; } = "";
	public string Gpu { get; set; } = "";

	[JsonPropertyName("avg_overall")]
	public double AvgOverall { get; set; }

	[JsonPropertyName("avg_build_quality")]
	public double AvgBuildQuality { get; set; }

	[JsonPropertyName("avg_screen")]
	public double AvgScreen { get; set; }

	[JsonPropertyName("avg_noise")]
	public double AvgNoise { get; set; }

	[JsonPropertyName("avg_performance")]
	public double AvgPerformance { get; set; }

	[JsonPropertyName("rating_count")]
	public int RatingCount { get; set; }

	/// <summary> 用于排序/展示的本地化总平均分 </summary>
	[JsonIgnore]
	public double DisplayScore => AvgOverall;
}

/// <summary> 台式机部件排行榜条目（部件聚合） </summary>
public sealed class DesktopLeaderboardEntry
{
	public string ComponentType { get; set; } = "";
	public string ComponentModel { get; set; } = "";

	[JsonPropertyName("avg_overall")]
	public double AvgOverall { get; set; }

	[JsonPropertyName("rating_count")]
	public int RatingCount { get; set; }

	[JsonIgnore]
	public string ComponentTypeLabel => RatingConstants.GetComponentTypeLabel(ComponentType);
}

/// <summary> 笔记本机型下的单条评价 </summary>
public sealed class LaptopReviewEntry
{
	public string Id { get; set; } = "";

	[JsonPropertyName("overall_score")]
	public int OverallScore { get; set; }

	[JsonPropertyName("build_quality_score")]
	public int BuildQualityScore { get; set; }

	[JsonPropertyName("screen_score")]
	public int ScreenScore { get; set; }

	[JsonPropertyName("noise_score")]
	public int NoiseScore { get; set; }

	[JsonPropertyName("performance_score")]
	public int PerformanceScore { get; set; }

	[JsonPropertyName("review_text")]
	public string? ReviewText { get; set; }

	public string Author { get; set; } = "匿名用户";

	[JsonPropertyName("created_at")]
	public long CreatedAtMs { get; set; }
}

/// <summary> 台式机部件下的单条评价 </summary>
public sealed class DesktopReviewEntry
{
	public string Id { get; set; } = "";

	[JsonPropertyName("overall_score")]
	public int OverallScore { get; set; }

	[JsonPropertyName("review_text")]
	public string? ReviewText { get; set; }

	public string Author { get; set; } = "匿名用户";

	[JsonPropertyName("created_at")]
	public long CreatedAtMs { get; set; }
}

/// <summary> 排行榜分页响应 </summary>
public sealed class LeaderboardPage<T>
{
	public int Total { get; set; }
	public int Page { get; set; }
	public int Limit { get; set; }
	public List<T> Entries { get; set; } = [];
}

/// <summary> 整体统计 </summary>
public sealed class RatingStats
{
	public LaptopRatingStats Laptop { get; set; } = new();
	public DesktopRatingStats Desktop { get; set; } = new();
}

public sealed class LaptopRatingStats
{
	[JsonPropertyName("ratings")]
	public int Ratings { get; set; }

	[JsonPropertyName("machines")]
	public int Machines { get; set; }
}

public sealed class DesktopRatingStats
{
	[JsonPropertyName("ratings")]
	public int Ratings { get; set; }

	[JsonPropertyName("byType")]
	public Dictionary<string, DesktopTypeStat> ByType { get; set; } = new();
}

public sealed class DesktopTypeStat
{
	[JsonPropertyName("ratings")]
	public int Ratings { get; set; }

	[JsonPropertyName("models")]
	public int Models { get; set; }
}

/// <summary> 台式机部件类型 → 中文显示名映射 </summary>
public static class RatingConstants
{
	public static readonly Dictionary<string, string> ComponentTypeLabelMap = new()
	{
		["cpu"] = "处理器",
		["gpu"] = "显卡",
		["memory"] = "内存",
		["motherboard"] = "主板",
		["disk"] = "硬盘",
		["cooler"] = "散热器",
		["psu"] = "电源",
		["case"] = "机箱",
		["monitor"] = "显示器",
		["other"] = "其他",
	};

	public static readonly IReadOnlyList<string> ComponentTypesInOrder =
	["cpu", "gpu", "memory", "motherboard", "disk", "cooler", "psu", "case", "monitor", "other"];

	public static string GetComponentTypeLabel(string type)
	{
		return ComponentTypeLabelMap.TryGetValue(type, out var label) ? label : type;
	}

	// 笔记本评分维度显示名
	public static readonly (string Key, string Label)[] LaptopDimensions =
	[
		("overall", "总评"),
		("buildQuality", "质感"),
		("screen", "屏幕"),
		("noise", "噪音"),
		("performance", "性能"),
	];

	// 笔记本评分排序选项：sortBy 值 → 标签
	public static readonly (string Key, string Label)[] LaptopSortOptions =
	[
		("overall", "总评"),
		("performance", "性能"),
		("buildQuality", "质感"),
		("screen", "屏幕"),
		("noise", "噪音"),
		("latest", "最新"),
		("count", "评价数"),
	];

	public static readonly (string Key, string Label)[] DesktopSortOptions =
	[
		("overall", "总评"),
		("latest", "最新"),
		("count", "评价数"),
	];
}