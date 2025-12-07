using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using RetroGameCoverDownloader.Models;

namespace RetroGameCoverDownloader.Services;

public class GitHubService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly RateLimiter _rateLimiter;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string MainRepoOwner = "libretro-thumbnails";
    private const string MainRepoName = "libretro-thumbnails";
    private const string MainRepoBranch = "master";
    private static readonly char[] Separator = new[] { '\r', '\n' };

    // 1. Expose the event wrapper
    public event Action<TimeSpan>? RateLimitHit
    {
        add => _rateLimiter.OnRateLimitHit += value;
        remove => _rateLimiter.OnRateLimitHit -= value;
    }

    public GitHubService(string? token)
    {
        _rateLimiter = new RateLimiter(!string.IsNullOrWhiteSpace(token));
        try
        {
            _httpClient = new HttpClient();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to initialize HttpClient in GitHubService.", ex);
        }

        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RetroGameCoverDownloader", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        if (!string.IsNullOrWhiteSpace(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);
        }
    }

    public async Task<List<SystemConfig>> GetAvailableSystemsAsync(Action<string> logAction)
    {
        var systems = new List<SystemConfig>();
        const string context = "[GetAvailableSystemsAsync] ";

        try
        {
            logAction("Fetching .gitmodules...");
            const string gitmodulesUrl = $"https://raw.githubusercontent.com/{MainRepoOwner}/{MainRepoName}/{MainRepoBranch}/.gitmodules";

            await _rateLimiter.WaitForSlotAsync();
            var gitmodulesContent = await _httpClient.GetStringAsync(gitmodulesUrl);
            var repoNameMap = ParseGitmodules(gitmodulesContent);

            logAction("Fetching main repository tree...");
            const string mainRepoApiUrl = $"https://api.github.com/repos/{MainRepoOwner}/{MainRepoName}/git/trees/{MainRepoBranch}?recursive=1";

            await _rateLimiter.WaitForSlotAsync();
            var jsonResponse = await _httpClient.GetStringAsync(mainRepoApiUrl);
            var tree = JsonSerializer.Deserialize<GitHubTree>(jsonResponse, _jsonOptions);

            if (tree?.Tree != null)
            {
                foreach (var item in tree.Tree.Where(i => i.Type == "commit"))
                {
                    if (repoNameMap.TryGetValue(item.Path, out var systemRepoName))
                    {
                        systems.Add(new SystemConfig(item.Path, MainRepoOwner, systemRepoName, "Named_Boxarts"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"{context}Error fetching systems: {ex.Message}";
            logAction(errorMsg);
            await BugReportService.LogErrorAsync(ex, $"{context}Failed to fetch available systems from GitHub.");
        }

        return systems;
    }

    public async Task<List<GitHubTreeItem>> GetSystemFilesAsync(SystemConfig system, Action<string> logAction)
    {
        var branches = new[] { "main", "master" };
        var context = $"[GetSystemFilesAsync] System: {system.SystemName} ";

        foreach (var branch in branches)
        {
            try
            {
                var apiUrl = $"https://api.github.com/repos/{system.Owner}/{system.Repo}/git/trees/{branch}?recursive=1";
                await _rateLimiter.WaitForSlotAsync();
                var json = await _httpClient.GetStringAsync(apiUrl);
                var tree = JsonSerializer.Deserialize<GitHubTree>(json, _jsonOptions);

                if (tree != null)
                {
                    // Filter for files in the specific folder (Named_Boxarts)
                    return tree.Tree
                        .Where(i => i.Type == "blob" && i.Path.StartsWith(system.FolderPath + "/", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                logAction($"{context}Branch '{branch}' not found, trying next...");
                continue; // Try next branch
            }
            catch (Exception ex)
            {
                var errorMsg = $"{context}Error fetching files: {ex.Message}";
                logAction(errorMsg);
                await BugReportService.LogErrorAsync(ex, $"{context}Exception while fetching system files.");
                break;
            }
        }

        return new List<GitHubTreeItem>();
    }

    public async Task<byte[]?> DownloadFileAsync(string url)
    {
        const string context = "[DownloadFileAsync] ";

        try
        {
            await _rateLimiter.WaitForSlotAsync();
            var data = await _httpClient.GetByteArrayAsync(url);

            if (data == null || data.Length == 0)
            {
                throw new InvalidOperationException($"Downloaded data is null or empty from URL: {url}");
            }

            return data;
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, $"{context}Failed to download file from URL: {url}");

            return null;
        }
    }

    private Dictionary<string, string> ParseGitmodules(string content)
    {
        const string context = "[ParseGitmodules] ";

        var map = new Dictionary<string, string>();
        var lines = content.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        string? currentPath = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            try
            {
                if (trimmed.StartsWith("path =", StringComparison.Ordinal))
                {
                    currentPath = trimmed.Split('=', 2)[1].Trim();
                }
                else if (trimmed.StartsWith("url =", StringComparison.Ordinal) && currentPath != null)
                {
                    var url = trimmed.Split('=', 2)[1].Trim();
                    var repo = url.Split('/').LastOrDefault()?.Replace(".git", "");
                    if (!string.IsNullOrEmpty(repo))
                    {
                        map[currentPath] = repo;
                    }

                    currentPath = null;
                }
            }
            catch (Exception ex)
            {
                // Log parsing errors but continue processing other lines
                var errorMsg = $"{context}Error parsing line '{trimmed}': {ex.Message}";
                Console.WriteLine(errorMsg);
                _ = BugReportService.LogErrorAsync(ex, $"{context}Exception parsing gitmodules line: {trimmed}");
                currentPath = null; // Reset to avoid corrupting next entry
            }
        }

        // Validate we found some systems
        if (map.Count == 0)
        {
            var ex = new InvalidOperationException("No systems were parsed from .gitmodules content.");
            _ = BugReportService.LogErrorAsync(ex, $"{context}ParseGitmodules returned empty result.");
            throw ex;
        }

        return map;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}