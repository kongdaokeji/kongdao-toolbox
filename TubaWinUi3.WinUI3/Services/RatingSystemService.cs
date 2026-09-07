using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class RatingSystemService
{
	public const string DefaultApiBase = "https://ratingapi.tubawinui3.cn";

	private const string SettingsKeyApiBase = "RatingApiBase";
	private const string SettingsKeyDeviceId = "RatingDeviceId";
	private const string SettingsKeyAuthor = "RatingAuthor";

	private static DateTime DeviceIdEpoch => new(2020, 1, 1);

	private static readonly HttpClient _http = new()
	{
		Timeout = TimeSpan.FromSeconds(30)
	};

	private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(2);
	private static readonly Dictionary<string, (DateTime Expires, object Data)> _cache = [];
	private static readonly object _cacheLock = new();

	static RatingSystemService()
	{
		_http.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-RatingSystem");
	}

	public static void InvalidateCache()
	{
		lock (_cacheLock) { _cache.Clear(); }
	}

	private static bool TryGetCache<T>(string key, out T? value) where T : class
	{
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(key, out var entry) && entry.Expires > DateTime.UtcNow)
			{
				value = entry.Data as T;
				return value is not null;
			}
			_cache.Remove(key);
		}
		value = null;
		return false;
	}

	private static void SetCache(string key, object data)
	{
		lock (_cacheLock) { _cache[key] = (DateTime.UtcNow + CacheTtl, data); }
	}

	public static string ApiBase
	{
		get
		{
			var v = AppSettings.Get(SettingsKeyApiBase);
			return string.IsNullOrWhiteSpace(v) ? DefaultApiBase : v!.TrimEnd('/');
		}
		set
		{
			if (string.IsNullOrWhiteSpace(value))
				AppSettings.Remove(SettingsKeyApiBase);
			else
				AppSettings.Set(SettingsKeyApiBase, value.TrimEnd('/'));
		}
	}

	public static string GetDeviceId()
	{
		var v = AppSettings.Get(SettingsKeyDeviceId);
		if (!string.IsNullOrWhiteSpace(v)) return v;

		v = Guid.NewGuid().ToString("N");
		AppSettings.Set(SettingsKeyDeviceId, v);
		return v;
	}

	public static string AuthorName
	{
		get => AppSettings.Get(SettingsKeyAuthor) ?? "匿名用户";
		set
		{
			var trimmed = value?.Trim();
			if (string.IsNullOrWhiteSpace(trimmed)) trimmed = "匿名用户";
			AppSettings.Set(SettingsKeyAuthor, trimmed!.Length > 40 ? trimmed[..40] : trimmed);
		}
	}

	private static string GetDeviceHash()
	{
		return GetDeviceId();
	}

	public static async Task<bool> PingAsync(CancellationToken ct = default)
	{
		try
		{
			using var resp = await _http.GetAsync($"{ApiBase}/api/health", ct);
			return resp.IsSuccessStatusCode;
		}
		catch { return false; }
	}

	public static async Task<RatingStats?> GetStatsAsync(bool forceRefresh = false, CancellationToken ct = default)
	{
		const string cacheKey = "stats";
		if (!forceRefresh && TryGetCache<RatingStats>(cacheKey, out var cached))
			return cached;
		try
		{
			using var resp = await _http.GetAsync($"{ApiBase}/api/stats", ct);
			if (!resp.IsSuccessStatusCode) return null;
			var body = await resp.Content.ReadAsStringAsync(ct);
			var result = JsonSerializer.Deserialize<RatingStats>(body);
			if (result is not null) SetCache(cacheKey, result);
			return result;
		}
		catch { return null; }
	}

	public sealed class LaptopRatingRequest
	{
		public string DeviceModel { get; set; } = "";
		public string Cpu { get; set; } = "";
		public string Gpu { get; set; } = "";
		public int OverallScore { get; set; }
		public int BuildQualityScore { get; set; }
		public int ScreenScore { get; set; }
		public int NoiseScore { get; set; }
		public int PerformanceScore { get; set; }
		public string? ReviewText { get; set; }
		public string Author { get; set; } = "匿名用户";
		public string DeviceHash { get; set; } = "";
	}

	public static async Task<(bool ok, string message)> SubmitLaptopRatingAsync(
		LaptopRatingRequest req, CancellationToken ct = default)
	{
		req.Author = string.IsNullOrWhiteSpace(req.Author) ? AuthorName : req.Author;
		req.DeviceHash = GetDeviceHash();
		var json = JsonSerializer.Serialize(req, JsonOpts);
		try
		{
			using var content = new StringContent(json, Encoding.UTF8, "application/json");
			using var resp = await _http.PostAsync($"{ApiBase}/api/ratings/laptop", content, ct);
			var body = await resp.Content.ReadAsStringAsync(ct);
			if (resp.IsSuccessStatusCode)
			{
				InvalidateCache();
				return (true, "");
			}
			var err = TryParseError(body);
			return (false, err ?? $"提交失败（HTTP {resp.StatusCode}）");
		}
		catch (Exception ex)
		{
			return (false, "网络错误：" + ex.Message);
		}
	}

	public static async Task<List<LaptopLeaderboardEntry>> GetLaptopLeaderboardAsync(
		string sortBy = "overall", int page = 1, int limit = 50, bool forceRefresh = false, CancellationToken ct = default)
	{
		var cacheKey = $"laptop_lb_{sortBy}_{page}_{limit}";
		if (!forceRefresh && TryGetCache<List<LaptopLeaderboardEntry>>(cacheKey, out var cached))
			return cached!;
		try
		{
			var url = $"{ApiBase}/api/ratings/laptop/leaderboard?sortBy={Uri.EscapeDataString(sortBy)}&page={page}&limit={limit}";
			using var resp = await _http.GetAsync(url, ct);
			if (!resp.IsSuccessStatusCode) return [];
			var body = await resp.Content.ReadAsStringAsync(ct);
			var pageData = JsonSerializer.Deserialize<LeaderboardPage<LaptopLeaderboardEntry>>(body, JsonOpts);
			var result = pageData?.Entries ?? [];
			if (result.Count > 0) SetCache(cacheKey, result);
			return result;
		}
		catch { return []; }
	}

	public static async Task<List<LaptopReviewEntry>> GetLaptopReviewsAsync(
		string deviceModel, string cpu, string gpu, bool forceRefresh = false, CancellationToken ct = default)
	{
		var cacheKey = $"laptop_rv_{deviceModel}_{cpu}_{gpu}";
		if (!forceRefresh && TryGetCache<List<LaptopReviewEntry>>(cacheKey, out var cached))
			return cached!;
		try
		{
			var url = $"{ApiBase}/api/ratings/laptop/reviews?deviceModel={Uri.EscapeDataString(deviceModel)}&cpu={Uri.EscapeDataString(cpu)}&gpu={Uri.EscapeDataString(gpu)}";
			using var resp = await _http.GetAsync(url, ct);
			if (!resp.IsSuccessStatusCode) return [];
			var body = await resp.Content.ReadAsStringAsync(ct);
			var data = JsonSerializer.Deserialize<LaptopReviewsResponse>(body, JsonOpts);
			var result = data?.Reviews ?? [];
			if (result.Count > 0) SetCache(cacheKey, result);
			return result;
		}
		catch { return []; }
	}

	public sealed class DesktopRatingRequest
	{
		public string ComponentType { get; set; } = "";
		public string ComponentModel { get; set; } = "";
		public int OverallScore { get; set; }
		public string? ReviewText { get; set; }
		public string Author { get; set; } = "匿名用户";
		public string DeviceHash { get; set; } = "";
	}

	public static async Task<(bool ok, string message)> SubmitDesktopRatingAsync(
		DesktopRatingRequest req, CancellationToken ct = default)
	{
		req.Author = string.IsNullOrWhiteSpace(req.Author) ? AuthorName : req.Author;
		req.DeviceHash = GetDeviceHash();
		var json = JsonSerializer.Serialize(req, JsonOpts);
		try
		{
			using var content = new StringContent(json, Encoding.UTF8, "application/json");
			using var resp = await _http.PostAsync($"{ApiBase}/api/ratings/desktop", content, ct);
			var body = await resp.Content.ReadAsStringAsync(ct);
			if (resp.IsSuccessStatusCode)
			{
				InvalidateCache();
				return (true, "");
			}
			var err = TryParseError(body);
			return (false, err ?? $"提交失败（HTTP {resp.StatusCode}）");
		}
		catch (Exception ex)
		{
			return (false, "网络错误：" + ex.Message);
		}
	}

	public static async Task<List<DesktopLeaderboardEntry>> GetDesktopLeaderboardAsync(
		string componentType, string sortBy = "overall", int page = 1, int limit = 50, bool forceRefresh = false, CancellationToken ct = default)
	{
		var cacheKey = $"desktop_lb_{componentType}_{sortBy}_{page}_{limit}";
		if (!forceRefresh && TryGetCache<List<DesktopLeaderboardEntry>>(cacheKey, out var cached))
			return cached!;
		try
		{
			var url = $"{ApiBase}/api/ratings/desktop/leaderboard?componentType={Uri.EscapeDataString(componentType)}&sortBy={Uri.EscapeDataString(sortBy)}&page={page}&limit={limit}";
			using var resp = await _http.GetAsync(url, ct);
			if (!resp.IsSuccessStatusCode) return [];
			var body = await resp.Content.ReadAsStringAsync(ct);
			var pageData = JsonSerializer.Deserialize<LeaderboardPage<DesktopLeaderboardEntry>>(body, JsonOpts);
			var result = pageData?.Entries ?? [];
			if (result.Count > 0) SetCache(cacheKey, result);
			return result;
		}
		catch { return []; }
	}

	public static async Task<List<DesktopReviewEntry>> GetDesktopReviewsAsync(
		string componentType, string componentModel, bool forceRefresh = false, CancellationToken ct = default)
	{
		var cacheKey = $"desktop_rv_{componentType}_{componentModel}";
		if (!forceRefresh && TryGetCache<List<DesktopReviewEntry>>(cacheKey, out var cached))
			return cached!;
		try
		{
			var url = $"{ApiBase}/api/ratings/desktop/reviews?componentType={Uri.EscapeDataString(componentType)}&componentModel={Uri.EscapeDataString(componentModel)}";
			using var resp = await _http.GetAsync(url, ct);
			if (!resp.IsSuccessStatusCode) return [];
			var body = await resp.Content.ReadAsStringAsync(ct);
			var data = JsonSerializer.Deserialize<DesktopReviewsResponse>(body, JsonOpts);
			var result = data?.Reviews ?? [];
			if (result.Count > 0) SetCache(cacheKey, result);
			return result;
		}
		catch { return []; }
	}

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private static string? TryParseError(string body)
	{
		try
		{
			using var doc = JsonDocument.Parse(body);
			if (doc.RootElement.TryGetProperty("error", out var err))
				return err.GetString();
		}
		catch { }
		return null;
	}

	private sealed class LaptopReviewsResponse
	{
		public List<LaptopReviewEntry> Reviews { get; set; } = [];
	}

	private sealed class DesktopReviewsResponse
	{
		public List<DesktopReviewEntry> Reviews { get; set; } = [];
	}
}