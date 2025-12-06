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
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RetroGameCoverScannerWpf", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        if (!string.IsNullOrWhiteSpace(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);
        }
    }

    public async Task<List<SystemConfig>> GetAvailableSystemsAsync(Action<string> logAction)
    {
        var systems = new List<SystemConfig>();
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
            logAction($"Error fetching systems: {ex.Message}");
        }

        return systems;
    }

    public async Task<List<GitHubTreeItem>> GetSystemFilesAsync(SystemConfig system, Action<string> logAction)
    {
        var branches = new[] { "main", "master" };
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
                continue; // Try next branch
            }
            catch (Exception ex)
            {
                logAction($"Error fetching files for {system.SystemName}: {ex.Message}");
                break;
            }
        }

        return new List<GitHubTreeItem>();
    }

    public async Task<byte[]?> DownloadFileAsync(string url)
    {
        try
        {
            await _rateLimiter.WaitForSlotAsync();
            return await _httpClient.GetByteArrayAsync(url);
        }
        catch
        {
            return null;
        }
    }

    private Dictionary<string, string> ParseGitmodules(string content)
    {
        var map = new Dictionary<string, string>();
        var lines = content.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        string? currentPath = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
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

        return map;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}
