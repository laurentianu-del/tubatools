using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// 硬件评分系统服务 —— 与 Cloudflare Worker + D1 后端通信。
/// API 地址通过 AppSettings 的 "RatingApiBase" 配置，默认为占位地址，部署后请替换。
/// </summary>
public static class RatingSystemService
{
	/// <summary>
	/// 评分系统后端 API 根地址。部署 rating-worker 后将此常量改为你的 *.workers.dev 地址。
	/// 也支持在运行时通过 AppSettings["RatingApiBase"] 覆盖。
	/// </summary>
	public const string DefaultApiBase = "https://ratingapi.tubawinui3.cn";

	private const string SettingsKeyApiBase = "RatingApiBase";
	private const string SettingsKeyDeviceId = "RatingDeviceId";
	private const string SettingsKeyAuthor = "RatingAuthor";

	private static DateTime DeviceIdEpoch => new(2020, 1, 1);

	private static readonly HttpClient _http = new()
	{
		Timeout = TimeSpan.FromSeconds(30)
	};

	static RatingSystemService()
	{
		_http.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-RatingSystem");
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

	/// <summary> 读取/生成当前设备唯一标识，用于防刷。 </summary>
	public static string GetDeviceId()
	{
		var v = AppSettings.Get(SettingsKeyDeviceId);
		if (!string.IsNullOrWhiteSpace(v)) return v;

		v = Guid.NewGuid().ToString("N");
		AppSettings.Set(SettingsKeyDeviceId, v);
		return v;
	}

	/// <summary> 获取/设置评分作者昵称。 </summary>
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

	// ---------------------------------------------------------------------
	// 健康检查
	// ---------------------------------------------------------------------
	public static async Task<bool> PingAsync(CancellationToken ct = default)
	{
		try
		{
			using var resp = await _http.GetAsync($"{ApiBase}/api/health", ct);
			return resp.IsSuccessStatusCode;
		}
		catch { return false; }
	}

	public static async Task<RatingStats?> GetStatsAsync(CancellationToken ct = default)
	{
		try
		{
			using var resp = await _http.GetAsync($"{ApiBase}/api/stats", ct);
			if (!resp.IsSuccessStatusCode) return null;
			var body = await resp.Content.ReadAsStringAsync(ct);
			return JsonSerializer.Deserialize<RatingStats>(body);
		}
		catch { return null; }
	}

	// ---------------------------------------------------------------------
	// 笔记本评分
	// ---------------------------------------------------------------------
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
				return (true, "");
			var err = TryParseError(body);
			return (false, err ?? $"提交失败（HTTP {resp.StatusCode}）");
		}
		catch (Exception ex)
		{
			return (false, "网络错误：" + ex.Message);
		}
	}

	public static async Task<List<LaptopLeaderboardEntry>> GetLaptopLeaderboardAsync(
		string sortBy = "overall", int page = 1, int limit = 50, CancellationToken ct = default)
	{
		try
		{
			var url = $"{ApiBase}/api/ratings/laptop/leaderboard?sortBy={Uri.EscapeDataString(sortBy)}&page={page}&limit={limit}";
			using var resp = await _http.GetAsync(url, ct);
			if (!resp.IsSuccessStatusCode) return [];
			var body = await resp.Content.ReadAsStringAsync(ct);
			var pageData = JsonSerializer.Deserialize<LeaderboardPage<LaptopLeaderboardEntry>>(body, JsonOpts);
			return pageData?.Entries ?? [];
		}
		catch { return []; }
	}

	public static async Task<List<LaptopReviewEntry>> GetLaptopReviewsAsync(
		string deviceModel, string cpu, string gpu, CancellationToken ct = default)
	{
		try
		{
			var url = $"{ApiBase}/api/ratings/laptop/reviews?deviceModel={Uri.EscapeDataString(deviceModel)}&cpu={Uri.EscapeDataString(cpu)}&gpu={Uri.EscapeDataString(gpu)}";
			using var resp = await _http.GetAsync(url, ct);
			if (!resp.IsSuccessStatusCode) return [];
			var body = await resp.Content.ReadAsStringAsync(ct);
			var data = JsonSerializer.Deserialize<LaptopReviewsResponse>(body, JsonOpts);
			return data?.Reviews ?? [];
		}
		catch { return []; }
	}

	// ---------------------------------------------------------------------
	// 台式机部件评分
	// ---------------------------------------------------------------------
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
				return (true, "");
			var err = TryParseError(body);
			return (false, err ?? $"提交失败（HTTP {resp.StatusCode}）");
		}
		catch (Exception ex)
		{
			return (false, "网络错误：" + ex.Message);
		}
	}

	public static async Task<List<DesktopLeaderboardEntry>> GetDesktopLeaderboardAsync(
		string componentType, string sortBy = "overall", int page = 1, int limit = 50, CancellationToken ct = default)
	{
		try
		{
			var url = $"{ApiBase}/api/ratings/desktop/leaderboard?componentType={Uri.EscapeDataString(componentType)}&sortBy={Uri.EscapeDataString(sortBy)}&page={page}&limit={limit}";
			using var resp = await _http.GetAsync(url, ct);
			if (!resp.IsSuccessStatusCode) return [];
			var body = await resp.Content.ReadAsStringAsync(ct);
			var pageData = JsonSerializer.Deserialize<LeaderboardPage<DesktopLeaderboardEntry>>(body, JsonOpts);
			return pageData?.Entries ?? [];
		}
		catch { return []; }
	}

	public static async Task<List<DesktopReviewEntry>> GetDesktopReviewsAsync(
		string componentType, string componentModel, CancellationToken ct = default)
	{
		try
		{
			var url = $"{ApiBase}/api/ratings/desktop/reviews?componentType={Uri.EscapeDataString(componentType)}&componentModel={Uri.EscapeDataString(componentModel)}";
			using var resp = await _http.GetAsync(url, ct);
			if (!resp.IsSuccessStatusCode) return [];
			var body = await resp.Content.ReadAsStringAsync(ct);
			var data = JsonSerializer.Deserialize<DesktopReviewsResponse>(body, JsonOpts);
			return data?.Reviews ?? [];
		}
		catch { return []; }
	}

	// ---------------------------------------------------------------------
	// JSON 辅助
	// ---------------------------------------------------------------------
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