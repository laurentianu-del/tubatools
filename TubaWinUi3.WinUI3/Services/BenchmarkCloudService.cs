using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class BenchmarkCloudService
{
	private const string UpstreamOwner = "luolangaga";
	private const string UpstreamRepo = "tubatoolsPlugin";
	private const string ReportsPath = "reports";
	private const string GitHubApiBase = "https://api.github.com/repos/luolangaga/tubatoolsPlugin";
	private const string GitCodeRawBase = "https://raw.githubusercontent.com/luolangaga/tubatoolsPlugin/main/reports";
	private const string LeaderboardRawUrl = "https://raw.githubusercontent.com/luolangaga/tubatoolsPlugin/main/leaderboard.json";

	private static readonly HttpClient _apiClient;
	private static List<BenchmarkReportEntry>? _cache;
	private static DateTimeOffset _cacheTime;
	private static readonly TimeSpan CacheDuration;
	private static BenchmarkLeaderboardData? _leaderboardCache;
	private static DateTimeOffset _leaderboardCacheTime;

	static BenchmarkCloudService()
	{
		_apiClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(30)
		};
		CacheDuration = TimeSpan.FromMinutes(10);
		_apiClient.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
	}

	public static void InvalidateCache()
	{
		_cache = null;
		_cacheTime = DateTimeOffset.MinValue;
		_leaderboardCache = null;
		_leaderboardCacheTime = DateTimeOffset.MinValue;
	}

	public static async Task<string> UploadReportAsync(PerformanceBenchmarkResult result, IProgress<string>? progress, CancellationToken ct)
	{
		if (!GitHubAuthService.IsLoggedIn)
		{
			throw new InvalidOperationException("请先登录 GitHub 账号");
		}
		string token = GitHubAuthService.GetToken() ?? throw new InvalidOperationException("GitHub Token 无效");
		var user = (await GitHubAuthService.GetCurrentUserAsync(ct)) ?? throw new InvalidOperationException("无法获取 GitHub 用户信息");
		var entry = ToReportEntry(result, user.Login);
		string json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
		{
			WriteIndented = false,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		});
		progress?.Report("正在 Fork 仓库...");
		string forkOwner = await EnsureForkAsync(token, ct);
		progress?.Report("正在同步 Fork...");
		await SyncForkWithUpstreamAsync(forkOwner, token, ct);
		string branchName = "report/" + entry.Id;
		progress?.Report("正在创建分支...");
		string mainSha = await GetRefShaAsync(forkOwner, "tubatoolsPlugin", "heads/main", token, ct);
		if (mainSha == null)
		{
			throw new InvalidOperationException("无法获取 main 分支 SHA");
		}
		if (await CheckRefExistsAsync(forkOwner, "tubatoolsPlugin", "heads/" + branchName, token, ct))
		{
			branchName = $"report/{entry.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
		}
		await CreateRefAsync(forkOwner, "tubatoolsPlugin", "refs/heads/" + branchName, mainSha, token, ct);
		progress?.Report("正在上传报告...");
		string path = $"{ReportsPath}/{entry.Author}/{entry.Id}.json";
		await CreateFileAsync(forkOwner, "tubatoolsPlugin", path, branchName, json, token, ct);
		progress?.Report("正在创建 PR...");
		return await CreatePullRequestAsync(branchName, forkOwner, entry, token, ct);
	}

	private static BenchmarkReportEntry ToReportEntry(PerformanceBenchmarkResult result, string author)
	{
		string id = $"{author}-{result.TestTime:yyyyMMdd-HHmmss}";
		return new BenchmarkReportEntry
		{
			Id = id,
			Author = author,
			SubmittedAt = DateTimeOffset.UtcNow,
			CpuName = result.CpuName,
			GpuName = result.GpuName,
			OsName = result.OsName,
			MotherboardName = result.MotherboardName,
			MemoryInfo = result.MemoryInfo,
			DiskInfo = result.DiskInfo,
			DisplayInfo = result.DisplayInfo,
			GamingScore = result.GamingScore,
			GamingGrade = result.GamingGrade,
			OfficeScore = result.OfficeScore,
			OfficeGrade = result.OfficeGrade,
			CpuSingleCoreScore = result.Cpu.SingleCoreScore,
			CpuMultiCoreScore = result.Cpu.MultiCoreScore,
			GpuRenderScore = result.Gpu.RenderScore,
			MemoryCapacityScore = result.Memory.CapacityScore,
			DiskSeqReadScore = result.Disk.SeqReadScore,
			DiskSeqWriteScore = result.Disk.SeqWriteScore,
			Disk4KReadScore = result.Disk.Random4KReadScore,
			Disk4KWriteScore = result.Disk.Random4KWriteScore,
			BrowserTotalScore = result.Browser.TotalScore
		};
	}

	public static async Task<string> DeleteReportAsync(BenchmarkReportEntry entry, IProgress<string>? progress, CancellationToken ct)
	{
		if (!GitHubAuthService.IsLoggedIn)
		{
			throw new InvalidOperationException("请先登录 GitHub 账号");
		}
		string token = GitHubAuthService.GetToken() ?? throw new InvalidOperationException("GitHub Token 无效");
		if ((await GitHubAuthService.GetCurrentUserAsync(ct) ?? throw new InvalidOperationException("无法获取 GitHub 用户信息")).Login != entry.Author)
		{
			throw new InvalidOperationException("只能删除自己上传的报告");
		}
		progress?.Report("正在 Fork 仓库...");
		string forkOwner = await EnsureForkAsync(token, ct);
		progress?.Report("正在同步 Fork...");
		await SyncForkWithUpstreamAsync(forkOwner, token, ct);
		string branchName = "delete/" + entry.Id;
		progress?.Report("正在创建分支...");
		string mainSha = await GetRefShaAsync(forkOwner, "tubatoolsPlugin", "heads/main", token, ct);
		if (mainSha == null)
		{
			throw new InvalidOperationException("无法获取 main 分支 SHA");
		}
		await CreateRefAsync(forkOwner, "tubatoolsPlugin", "refs/heads/" + branchName, mainSha, token, ct);
		progress?.Report("正在删除文件...");
		await DeleteFileAsync(forkOwner, "tubatoolsPlugin", entry.RepoPath, branchName, token, ct);
		progress?.Report("正在创建 PR...");
		string prUrl = await CreateDeletePullRequestAsync(branchName, forkOwner, entry, token, ct);
		InvalidateCache();
		return prUrl;
	}

	public static async Task<List<BenchmarkReportEntry>> GetAllReportsAsync(CancellationToken ct)
	{
		if (_cache != null && DateTimeOffset.UtcNow - _cacheTime < CacheDuration)
		{
			return _cache;
		}
		try
		{
			var leaderboardData = await GetLeaderboardDataAsync(ct);
			if (leaderboardData != null && leaderboardData.Leaderboards.TryGetValue("gaming", out var gamingList))
			{
				var reports = gamingList.Select(e => e.ToReportEntry()).ToList();
				SaveLocalCache(reports);
				_cache = reports;
				_cacheTime = DateTimeOffset.UtcNow;
				return _cache;
			}
		}
		catch
		{
		}
		return await GetAllReportsFallbackAsync(ct);
	}

	public static async Task<BenchmarkLeaderboardData?> GetLeaderboardDataAsync(CancellationToken ct)
	{
		if (_leaderboardCache != null && DateTimeOffset.UtcNow - _leaderboardCacheTime < CacheDuration)
		{
			return _leaderboardCache;
		}
		try
		{
			using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
			client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
			var resp = await client.GetAsync(LeaderboardRawUrl, ct);
			if (!resp.IsSuccessStatusCode) return null;
			string json = await resp.Content.ReadAsStringAsync(ct);
			var data = JsonSerializer.Deserialize<BenchmarkLeaderboardData>(json, new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			});
			if (data != null)
			{
				_leaderboardCache = data;
				_leaderboardCacheTime = DateTimeOffset.UtcNow;
			}
			return data;
		}
		catch
		{
			return null;
		}
	}

	public static async Task<List<BenchmarkLeaderboardEntry>> GetLeaderboardAsync(string sortBy, string? cpuFilter = null, string? gpuFilter = null, CancellationToken ct = default)
	{
		var data = await GetLeaderboardDataAsync(ct);
		if (data == null)
		{
			var allReports = await GetAllReportsAsync(ct);
			return ComputeLeaderboard(allReports, sortBy, cpuFilter, gpuFilter);
		}
		if (!data.Leaderboards.TryGetValue(sortBy, out var entries))
		{
			entries = data.Leaderboards.GetValueOrDefault("gaming", []);
		}
		IEnumerable<BenchmarkLeaderboardRankEntry> source = entries;
		if (!string.IsNullOrWhiteSpace(cpuFilter))
		{
			source = source.Where(e => e.CpuName.Contains(cpuFilter, StringComparison.OrdinalIgnoreCase));
		}
		if (!string.IsNullOrWhiteSpace(gpuFilter))
		{
			source = source.Where(e => e.GpuName.Contains(gpuFilter, StringComparison.OrdinalIgnoreCase));
		}
		return source.Select((e, i) => new BenchmarkLeaderboardEntry
		{
			Rank = i + 1,
			Report = e.ToReportEntry()
		}).ToList();
	}

	private static async Task<List<BenchmarkReportEntry>> GetAllReportsFallbackAsync(CancellationToken ct)
	{
		var reports = new ConcurrentBag<BenchmarkReportEntry>();
		try
		{
			var localCache = LoadLocalCache();
			string treeSha = await GetLatestTreeShaAsync(ct);
			if (treeSha == null)
			{
				if (localCache.Count > 0)
				{
					_cache = localCache.OrderByDescending(r => r.GamingScore).ToList();
					_cacheTime = DateTimeOffset.UtcNow;
					return _cache;
				}
				return [];
			}
			var allFiles = await GetRecursiveTreeBlobsAsync(treeSha, "reports", ct);
			if (allFiles.Count == 0)
			{
				if (localCache.Count > 0)
				{
					_cache = localCache.OrderByDescending(r => r.GamingScore).ToList();
					_cacheTime = DateTimeOffset.UtcNow;
					return _cache;
				}
				return [];
			}
			var toDownload = new List<(string Path, string Sha)>();
			var cachedSet = new HashSet<string>(localCache.Select(r => r.RepoPath));
			foreach (var (path, sha) in allFiles)
			{
				if (cachedSet.Contains(path))
				{
					var cached = localCache.FirstOrDefault(r => r.RepoPath == path);
					if (cached != null) reports.Add(cached);
				}
				else
				{
					toDownload.Add((path, sha));
				}
			}
			if (toDownload.Count > 0)
			{
				await Parallel.ForEachAsync(toDownload, new ParallelOptions
				{
					MaxDegreeOfParallelism = 6,
					CancellationToken = ct
				}, async (item, token) =>
				{
					try
					{
						string content = await DownloadBlobAsync(item.Sha, token);
						if (content != null)
						{
							var entry = JsonSerializer.Deserialize<BenchmarkReportEntry>(content, new JsonSerializerOptions
							{
								PropertyNamingPolicy = JsonNamingPolicy.CamelCase
							});
							if (entry != null)
							{
								entry.RepoPath = item.Path;
								reports.Add(entry);
							}
						}
					}
					catch
					{
					}
				});
			}
			SaveLocalCache(reports.ToList());
		}
		catch
		{
		}
		_cache = reports.OrderByDescending(r => r.GamingScore).ToList();
		_cacheTime = DateTimeOffset.UtcNow;
		return _cache;
	}

	private static string LocalCachePath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"TubaWinUi3", "benchmark_cache.json");

	private static List<BenchmarkReportEntry> LoadLocalCache()
	{
		try
		{
			if (!File.Exists(LocalCachePath)) return [];
			string json = File.ReadAllText(LocalCachePath);
			return JsonSerializer.Deserialize<List<BenchmarkReportEntry>>(json, new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			}) ?? [];
		}
		catch
		{
			return [];
		}
	}

	private static void SaveLocalCache(List<BenchmarkReportEntry> reports)
	{
		try
		{
			var dir = Path.GetDirectoryName(LocalCachePath);
			if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
			string json = JsonSerializer.Serialize(reports, new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				WriteIndented = false
			});
			File.WriteAllText(LocalCachePath, json);
		}
		catch
		{
		}
	}

	private static async Task<List<(string Path, string Sha)>> GetRecursiveTreeBlobsAsync(string treeSha, string prefix, CancellationToken ct)
	{
		var result = new List<(string, string)>();
		using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
		client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
		string url = $"https://api.github.com/repos/luolangaga/tubatoolsPlugin/git/trees/{treeSha}?recursive=1";
		string json = await client.GetStringAsync(url, ct);
		using var doc = JsonDocument.Parse(json);
		if (!doc.RootElement.TryGetProperty("tree", out var tree)) return result;
		bool inReports = false;
		foreach (var item in tree.EnumerateArray())
		{
			string path = item.GetProperty("path").GetString() ?? "";
			string type = item.GetProperty("type").GetString() ?? "";
			if (!inReports && (path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal)))
			{
				inReports = true;
			}
			if (inReports && type == "blob" && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			{
				string sha = item.GetProperty("sha").GetString() ?? "";
				result.Add((path, sha));
			}
			if (inReports && !path.StartsWith(prefix + "/", StringComparison.Ordinal) && path != prefix)
			{
				break;
			}
		}
		return result;
	}

	public static List<BenchmarkLeaderboardEntry> ComputeLeaderboard(List<BenchmarkReportEntry> reports, string sortBy, string? cpuFilter = null, string? gpuFilter = null)
	{
		IEnumerable<BenchmarkReportEntry> source = reports.AsEnumerable();
		if (!string.IsNullOrWhiteSpace(cpuFilter))
		{
			source = source.Where(r => r.CpuName.Contains(cpuFilter, StringComparison.OrdinalIgnoreCase));
		}
		if (!string.IsNullOrWhiteSpace(gpuFilter))
		{
			source = source.Where(r => r.GpuName.Contains(gpuFilter, StringComparison.OrdinalIgnoreCase));
		}
		return (sortBy switch
		{
			"gaming" => source.OrderByDescending(r => r.GamingScore),
			"office" => source.OrderByDescending(r => r.OfficeScore),
			"cpu" => source.OrderByDescending(r => r.CpuMultiCoreScore),
			"gpu" => source.OrderByDescending(r => r.GpuRenderScore),
			"disk" => source.OrderByDescending(r => r.DiskSeqReadScore),
			"browser" => source.OrderByDescending(r => r.BrowserTotalScore),
			_ => source.OrderByDescending(r => r.GamingScore),
		}).ToList().Select((r, i) => new BenchmarkLeaderboardEntry
		{
			Rank = i + 1,
			Report = r
		}).ToList();
	}

	public static List<BenchmarkLeaderboardEntry> ComputeSameHardwareLeaderboard(List<BenchmarkReportEntry> reports, string cpuName, string gpuName)
	{
		return (from r in reports
			where IsSameHardware(r.CpuName, cpuName) && IsSameHardware(r.GpuName, gpuName)
			orderby r.GamingScore descending
			select r).ToList().Select((r, i) => new BenchmarkLeaderboardEntry
		{
			Rank = i + 1,
			Report = r
		}).ToList();
	}

	private static bool IsSameHardware(string a, string b)
	{
		if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
		{
			return false;
		}
		string sa = Simplify(a);
		string sb = Simplify(b);
		if (sa == sb || sa.Contains(sb, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return sb.Contains(sa, StringComparison.OrdinalIgnoreCase);
	}

	private static string Simplify(string s)
	{
		return s.Replace("(R)", "").Replace("(TM)", "").Replace("(C)", "")
			.Replace("@", "")
			.Replace("CPU", "")
			.Replace("Processor", "")
			.Replace("Graphics", "")
			.Replace("GPU", "")
			.Trim();
	}

	public static async Task<List<BenchmarkReportEntry>> GetMyReportsAsync(CancellationToken ct)
	{
		if (!GitHubAuthService.IsLoggedIn)
		{
			return [];
		}
		var user = await GitHubAuthService.GetCurrentUserAsync(ct);
		if (user == null)
		{
			return [];
		}
		return (from r in await GetAllReportsAsync(ct)
			where r.Author == user.Login
			orderby r.SubmittedAt descending
			select r).ToList();
	}

	private static async Task<string> EnsureForkAsync(string token, CancellationToken ct)
	{
		string forkOwner = (await GitHubAuthService.GetCurrentUserAsync(ct))?.Login ?? throw new InvalidOperationException("无法获取用户名");
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		try
		{
			if ((await client.GetAsync($"https://api.github.com/repos/{forkOwner}/tubatoolsPlugin", ct)).IsSuccessStatusCode)
			{
				return forkOwner;
			}
		}
		catch
		{
		}
		var forkResp = await client.PostAsync("https://api.github.com/repos/luolangaga/tubatoolsPlugin/forks", new StringContent("{}", Encoding.UTF8, "application/json"), ct);
		if (!forkResp.IsSuccessStatusCode)
		{
			string body = await forkResp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"Fork 失败：{(int)forkResp.StatusCode} {forkResp.StatusCode}\n{body}");
		}
		await Task.Delay(3000, ct);
		return forkOwner;
	}

	private static async Task SyncForkWithUpstreamAsync(string forkOwner, string token, CancellationToken ct)
	{
		string upstreamMainSha = await GetRefShaAsync("luolangaga", "tubatoolsPlugin", "heads/main", token, ct);
		if (upstreamMainSha == null || await GetRefShaAsync(forkOwner, "tubatoolsPlugin", "heads/main", token, ct) == upstreamMainSha)
		{
			return;
		}
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		var content = new StringContent(JsonSerializer.Serialize(new
		{
			sha = upstreamMainSha,
			force = true
		}), Encoding.UTF8, "application/json");
		await client.PatchAsync($"https://api.github.com/repos/{forkOwner}/{UpstreamRepo}/git/refs/heads/main", content, ct);
	}

	private static async Task<string?> GetLatestTreeShaAsync(CancellationToken ct)
	{
		using var client = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(30)
		};
		client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
		return JsonDocument.Parse(await client.GetStringAsync("https://api.github.com/repos/luolangaga/tubatoolsPlugin/git/ref/heads/main", ct)).RootElement.GetProperty("object").GetProperty("sha").GetString();
	}

	private static async Task<string?> DownloadBlobAsync(string sha, CancellationToken ct)
	{
		using var client = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(30)
		};
		client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
		if (JsonDocument.Parse(await client.GetStringAsync("https://api.github.com/repos/luolangaga/tubatoolsPlugin/git/blobs/" + sha, ct)).RootElement.TryGetProperty("content", out var content))
		{
			byte[] bytes = Convert.FromBase64String((content.GetString() ?? "").Replace("\n", "").Replace("\r", ""));
			return Encoding.UTF8.GetString(bytes);
		}
		return null;
	}

	private static async Task<string?> GetRefShaAsync(string owner, string repo, string refPath, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		try
		{
			return JsonDocument.Parse(await client.GetStringAsync($"https://api.github.com/repos/{owner}/{repo}/git/ref/{refPath}", ct)).RootElement.GetProperty("object").GetProperty("sha").GetString();
		}
		catch
		{
			return null;
		}
	}

	private static async Task<bool> CheckRefExistsAsync(string owner, string repo, string refPath, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		try
		{
			return (await client.GetAsync($"https://api.github.com/repos/{owner}/{repo}/git/ref/{refPath}", ct)).IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	private static async Task CreateRefAsync(string owner, string repo, string refName, string sha, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		var content = new StringContent(JsonSerializer.Serialize(new
		{
			@ref = refName,
			sha = sha
		}), Encoding.UTF8, "application/json");
		var resp = await client.PostAsync($"https://api.github.com/repos/{owner}/{repo}/git/refs", content, ct);
		if (!resp.IsSuccessStatusCode)
		{
			string body = await resp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"创建分支失败：{(int)resp.StatusCode}\n{body}");
		}
	}

	private static async Task CreateFileAsync(string owner, string repo, string path, string branch, string content, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		string base64Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
		var requestContent = new StringContent(JsonSerializer.Serialize(new
		{
			message = "benchmark: upload report - " + Path.GetFileNameWithoutExtension(path),
			content = base64Content,
			branch = branch
		}), Encoding.UTF8, "application/json");
		var resp = await client.PutAsync($"https://api.github.com/repos/{owner}/{repo}/contents/{path}", requestContent, ct);
		if (!resp.IsSuccessStatusCode)
		{
			string body = await resp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"上传报告失败：{(int)resp.StatusCode}\n{body}");
		}
	}

	private static async Task<string> CreatePullRequestAsync(string branch, string forkOwner, BenchmarkReportEntry entry, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		string body = $"## 性能测试报告上传\n\n- **CPU**：{entry.CpuName}\n- **GPU**：{entry.GpuName}\n- **游戏性能**：{entry.GamingScore} ({entry.GamingGrade})\n- **办公性能**：{entry.OfficeScore} ({entry.OfficeGrade})\n- **提交者**：@{entry.Author}\n";
		var content = new StringContent(JsonSerializer.Serialize(new
		{
			title = "[性能报告] " + entry.CpuName + " / " + entry.GpuName,
			head = forkOwner + ":" + branch,
			@base = "main",
			body = body
		}), Encoding.UTF8, "application/json");
		var resp = await client.PostAsync("https://api.github.com/repos/luolangaga/tubatoolsPlugin/pulls", content, ct);
		if (!resp.IsSuccessStatusCode)
		{
			string respBody = await resp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"创建 PR 失败：{(int)resp.StatusCode}\n{respBody}");
		}
		return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("html_url").GetString() ?? "";
	}

	private static async Task DeleteFileAsync(string owner, string repo, string path, string branch, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		var getResp = await client.GetAsync($"https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}", ct);
		if (!getResp.IsSuccessStatusCode)
		{
			string body = await getResp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"获取文件信息失败：{(int)getResp.StatusCode}\n{body}");
		}
		string fileSha = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("sha").GetString() ?? "";
		var content = new StringContent(JsonSerializer.Serialize(new
		{
			message = "benchmark: delete report - " + Path.GetFileNameWithoutExtension(path),
			sha = fileSha,
			branch = branch
		}), Encoding.UTF8, "application/json");
		var request = new HttpRequestMessage(HttpMethod.Delete, $"https://api.github.com/repos/{owner}/{repo}/contents/{path}")
		{
			Content = content
		};
		var resp = await client.SendAsync(request, ct);
		if (!resp.IsSuccessStatusCode)
		{
			string body2 = await resp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"删除文件失败：{(int)resp.StatusCode}\n{body2}");
		}
	}

	private static async Task<string> CreateDeletePullRequestAsync(string branch, string forkOwner, BenchmarkReportEntry entry, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		string body = $"## 删除性能测试报告\n\n- **报告ID**：{entry.Id}\n- **CPU**：{entry.CpuName}\n- **GPU**：{entry.GpuName}\n- **提交者**：@{entry.Author}\n";
		var content = new StringContent(JsonSerializer.Serialize(new
		{
			title = "[删除报告] " + entry.CpuName + " / " + entry.GpuName,
			head = forkOwner + ":" + branch,
			@base = "main",
			body = body
		}), Encoding.UTF8, "application/json");
		var resp = await client.PostAsync("https://api.github.com/repos/luolangaga/tubatoolsPlugin/pulls", content, ct);
		if (!resp.IsSuccessStatusCode)
		{
			string respBody = await resp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"创建 PR 失败：{(int)resp.StatusCode}\n{respBody}");
		}
		return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("html_url").GetString() ?? "";
	}
}
