using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using RetroGameCoverDownloader.Models;

namespace RetroGameCoverDownloader.Services;

public class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;
    private readonly RateLimiter _rateLimiter;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string MainRepoOwner = "libretro-thumbnails";
    private const string MainRepoName = "libretro-thumbnails";
    private static readonly char[] Separator = ['\r', '\n'];

    // Circuit breaker state (thread-safe counters for 503 tracking)
    private int _consecutive503Count;
    private DateTime _circuitBreakerOpenUntil = DateTime.MinValue;

    // 1. Expose the event wrapper
    public event Action<TimeSpan>? RateLimitHit
    {
        add => _rateLimiter.OnRateLimitHit += value;
        remove => _rateLimiter.OnRateLimitHit -= value;
    }

    // Internal constructor for testability (allows injecting a mock HttpClient)
    internal GitHubService(HttpClient httpClient, RateLimiter? rateLimiter = null)
    {
        _httpClient = httpClient;
        _rateLimiter = rateLimiter ?? new RateLimiter(false);
    }

    public GitHubService(string? token, bool useProxy = false, string? proxyHost = null, int proxyPort = 0, string? proxyUsername = null, string? proxyPassword = null)
    {
        _rateLimiter = new RateLimiter(!string.IsNullOrWhiteSpace(token));

        var handler = new HttpClientHandler();

        // Configure proxy if enabled
        if (useProxy && !string.IsNullOrWhiteSpace(proxyHost) && proxyPort > 0)
        {
            // Strip protocol prefix if user accidentally included it
            var cleanHost = proxyHost.Trim();
            if (cleanHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                cleanHost = cleanHost["http://".Length..];
            }
            else if (cleanHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                cleanHost = cleanHost["https://".Length..];
            }

            cleanHost = cleanHost.TrimEnd('/');

            var proxy = new WebProxy
            {
                Address = new Uri($"http://{cleanHost}:{proxyPort}"),
                BypassProxyOnLocal = false
            };

            // Add credentials only if both username and password are provided
            if (!string.IsNullOrWhiteSpace(proxyUsername) && !string.IsNullOrWhiteSpace(proxyPassword))
            {
                proxy.Credentials = new NetworkCredential(proxyUsername, proxyPassword);
            }

            handler.Proxy = proxy;
            handler.UseProxy = true;
        }

        try
        {
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to initialize HttpClient in GitHubService.", ex);
        }

        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RetroGameCoverDownloader", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        UpdateAuthorizationHeader(token);
    }

    private void UpdateAuthorizationHeader(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            var scheme = token.StartsWith("ghp_", StringComparison.OrdinalIgnoreCase)
                         || token.StartsWith("github_pat_", StringComparison.OrdinalIgnoreCase)
                ? "Bearer"
                : "token";
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(scheme, token);
        }
        else
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    public async Task<List<SystemConfig>> GetAvailableSystemsAsync(Action<string> logAction, CancellationToken cancellationToken = default)
    {
        const string context = "[GetAvailableSystemsAsync] ";

        var branches = new[] { "master", "main" };
        Exception? lastException = null;

        foreach (var branch in branches)
        {
            try
            {
                var systems = await TryFetchSystemsForBranchAsync(branch, logAction, cancellationToken);
                if (systems.Count > 0)
                {
                    await SaveSystemsToCacheAsync(systems);
                    return systems;
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                var errorMsg = $"{context}GitHub API rate limit exceeded on branch '{branch}'. {ex.Message}";
                logAction(errorMsg);

                var cached = await LoadSystemsFromCacheAsync();
                if (cached != null)
                {
                    logAction("Using cached system list due to rate limiting.");
                    return cached;
                }

                logAction("No cached system list available.");
                throw;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                lastException = ex;
                logAction($"{context}Branch '{branch}' not found (404), trying next branch...");
            }
            catch (Exception ex)
            {
                lastException = ex;
                logAction($"{context}Error fetching systems on branch '{branch}': {ex.Message}");
            }
        }

        var fallbackCached = await LoadSystemsFromCacheAsync();
        if (fallbackCached != null)
        {
            logAction("Using cached system list due to error.");
            return fallbackCached;
        }

        if (lastException != null)
        {
            await BugReportService.LogErrorAsync(lastException, $"{context}Failed to fetch available systems from GitHub.");
        }

        return new List<SystemConfig>();
    }

    private async Task<List<SystemConfig>> TryFetchSystemsForBranchAsync(string branch, Action<string> logAction, CancellationToken cancellationToken)
    {
        var systems = new List<SystemConfig>();

        logAction($"Fetching .gitmodules from branch '{branch}'...");
        var gitmodulesContent = await FetchGitmodulesAsync(branch, logAction, cancellationToken);
        var repoNameMap = ParseGitmodules(gitmodulesContent);

        logAction("Fetching main repository tree...");
        var mainRepoApiUrl = $"https://api.github.com/repos/{MainRepoOwner}/{MainRepoName}/git/trees/{branch}?recursive=1";

        await _rateLimiter.WaitForSlotAsync(cancellationToken);
        var jsonResponse = await RetryOnTransientErrorAsync(() => _httpClient.GetStringAsync(mainRepoApiUrl, cancellationToken), cancellationToken: cancellationToken);
        var tree = JsonSerializer.Deserialize<GitHubTree>(jsonResponse, _jsonOptions);

        if (tree?.Tree != null)
        {
            foreach (var item in tree.Tree.Where(static i => i.Type == "commit"))
            {
                if (repoNameMap.TryGetValue(item.Path, out var systemRepoName))
                {
                    systems.Add(new SystemConfig(item.Path, MainRepoOwner, systemRepoName, "Named_Boxarts"));
                }
            }
        }

        return systems;
    }

    private async Task<string> FetchGitmodulesAsync(string branch, Action<string> logAction, CancellationToken cancellationToken)
    {
        var rawUrl = $"https://raw.githubusercontent.com/{MainRepoOwner}/{MainRepoName}/{branch}/.gitmodules";

        try
        {
            await _rateLimiter.WaitForSlotAsync(cancellationToken);
            return await RetryOnTransientErrorAsync(() => _httpClient.GetStringAsync(rawUrl, cancellationToken), cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logAction($"[FetchGitmodulesAsync] raw.githubusercontent.com failed ({ex.Message}), trying GitHub Contents API...");

            var contentsApiUrl = $"https://api.github.com/repos/{MainRepoOwner}/{MainRepoName}/contents/.gitmodules?ref={branch}";
            await _rateLimiter.WaitForSlotAsync(cancellationToken);
            var json = await RetryOnTransientErrorAsync(() => _httpClient.GetStringAsync(contentsApiUrl, cancellationToken), cancellationToken: cancellationToken);

            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement.GetProperty("content").GetString();
            var encoding = doc.RootElement.TryGetProperty("encoding", out var encProp) ? encProp.GetString() : "base64";

            if (string.IsNullOrEmpty(content))
                throw new InvalidOperationException("GitHub Contents API returned empty content for .gitmodules.");

            if (string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = Convert.FromBase64String(content.Replace("\n", "").Replace("\r", ""));
                return System.Text.Encoding.UTF8.GetString(bytes);
            }

            return content;
        }
    }

    public async Task<(string Branch, List<GitHubTreeItem> Files)> GetSystemFilesAsync(SystemConfig system, Action<string> logAction, CancellationToken cancellationToken = default)
    {
        var branches = new[] { "main", "master" };
        var context = $"[GetSystemFilesAsync] System: {system.SystemName} ";

        foreach (var branch in branches)
        {
            try
            {
                // Step 1: Try the recursive approach (efficient for small/medium repos)
                var apiUrl = $"https://api.github.com/repos/{system.Owner}/{system.Repo}/git/trees/{branch}?recursive=1";
                await _rateLimiter.WaitForSlotAsync(cancellationToken);

                using var response = await RetryOnTransientErrorAsync(() => _httpClient.GetAsync(apiUrl, cancellationToken), cancellationToken: cancellationToken);

                switch (response.StatusCode)
                {
                    // Handle the 500 error specifically for large repos (like PS2)
                    case HttpStatusCode.InternalServerError:
                        logAction($"{context}Repository too large for recursive fetch. Attempting non-recursive fallback...");
                        return await GetSystemFilesLargeRepoFallbackAsync(system, branch, logAction, cancellationToken);
                    case HttpStatusCode.NotFound:
                        continue; // Try next branch
                }

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var tree = JsonSerializer.Deserialize<GitHubTree>(json, _jsonOptions);

                if (tree?.Tree != null)
                {
                    var files = tree.Tree
                        .Where(i => i.Type == "blob" && i.Path.StartsWith(system.FolderPath + "/", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (files.Count > 0) return (branch, files);
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"{context}Error fetching files: {ex.Message}";
                logAction(errorMsg);
                await BugReportService.LogErrorAsync(ex, $"{context}Exception while fetching system files on branch '{branch}', trying next branch.");
            }
        }

        return (string.Empty, new List<GitHubTreeItem>());
    }

    /// <summary>
    /// Fallback for large repositories where recursive calls fail with 500 errors.
    /// Fetches the root, finds the target folder SHA, and fetches that folder's tree specifically.
    /// </summary>
    private async Task<(string Branch, List<GitHubTreeItem> Files)> GetSystemFilesLargeRepoFallbackAsync(SystemConfig system, string branch, Action<string> logAction, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Get root tree (non-recursive)
            var rootUrl = $"https://api.github.com/repos/{system.Owner}/{system.Repo}/git/trees/{branch}";
            await _rateLimiter.WaitForSlotAsync(cancellationToken);
            var rootJson = await RetryOnTransientErrorAsync(() => _httpClient.GetStringAsync(rootUrl, cancellationToken), cancellationToken: cancellationToken);
            var rootTree = JsonSerializer.Deserialize<GitHubTree>(rootJson, _jsonOptions);

            // 2. Find the "Named_Boxarts" folder entry
            var folderEntry = rootTree?.Tree.FirstOrDefault(i =>
                i.Type == "tree" && string.Equals(i.Path, system.FolderPath, StringComparison.OrdinalIgnoreCase));

            if (folderEntry != null && !string.IsNullOrEmpty(folderEntry.Sha))
            {
                // 3. Fetch the tree for that specific folder (non-recursive is usually enough)
                // We use the SHA of the folder to get its contents directly
                var folderUrl = $"https://api.github.com/repos/{system.Owner}/{system.Repo}/git/trees/{folderEntry.Sha}";
                await _rateLimiter.WaitForSlotAsync(cancellationToken);
                var folderJson = await RetryOnTransientErrorAsync(() => _httpClient.GetStringAsync(folderUrl, cancellationToken), cancellationToken: cancellationToken);
                var folderTree = JsonSerializer.Deserialize<GitHubTree>(folderJson, _jsonOptions);

                if (folderTree?.Tree != null)
                {
                    // Map paths to include the folder prefix so the rest of the app logic remains compatible
                    var files = folderTree.Tree
                        .Where(static i => i.Type == "blob")
                        .Select(i => new GitHubTreeItem { Path = $"{system.FolderPath}/{i.Path}", Type = i.Type, Mode = i.Mode })
                        .ToList();

                    logAction($"Successfully retrieved {files.Count} files via fallback method.");
                    return (branch, files);
                }
            }
        }
        catch (Exception ex)
        {
            logAction($"Fallback failed: {ex.Message}");
            await BugReportService.LogErrorAsync(ex, "GetSystemFilesLargeRepoFallbackAsync failed.");
        }

        return (string.Empty, new List<GitHubTreeItem>());
    }


    public async Task<byte[]?> DownloadFileAsync(string url, Action<string>? logAction = null, CancellationToken cancellationToken = default)
    {
        const string context = "[DownloadFileAsync] ";
        const int maxRetries = 3;

        // Feature 1: Circuit Breaker - Check if we need to pause before attempting
        await WaitForCircuitBreakerAsync(logAction, cancellationToken);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await _rateLimiter.WaitForSlotAsync(cancellationToken);

                // Feature 2: User Feedback - Show current attempt
                logAction?.Invoke($"Downloading attempt {attempt}...");

                var data = await _httpClient.GetByteArrayAsync(url, cancellationToken);

                if (data.Length == 0)
                {
                    throw new InvalidOperationException($"Downloaded data is empty from URL: {url}");
                }

                // Success: Reset consecutive error count (closed circuit)
                Interlocked.Exchange(ref _consecutive503Count, 0);
                return data;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < maxRetries)
            {
                // Feature 1: Circuit Breaker Pattern - Track 503s
                var currentCount = Interlocked.Increment(ref _consecutive503Count);

                if (currentCount >= 5)
                {
                    // Use CompareExchange to ensure only one thread triggers the cooldown
                    if (Interlocked.CompareExchange(ref _consecutive503Count, 0, currentCount) == currentCount)
                    {
                        _circuitBreakerOpenUntil = DateTime.UtcNow.AddSeconds(30);
                        logAction?.Invoke("⚠️ Circuit breaker triggered: 5 consecutive 503s detected. Cooling down for 30s...");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
                else
                {
                    // Exponential backoff: 3s, 6s, 12s...
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 1.5);

                    // Feature 2: User Feedback - Show retry status with 503 count
                    logAction?.Invoke($"Server busy (503 attempt #{currentCount}). Retrying in {delay.TotalSeconds:F0}s...");
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException && attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 1.5);

                // Feature 2: User Feedback - Show timeout retry
                logAction?.Invoke($"Download timeout. Retrying in {delay.TotalSeconds:F0}s...");
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log final failure after all retries exhausted or non-transient error
                await BugReportService.LogErrorAsync(ex, $"{context}Failed after {attempt} attempts: {url}");
                return null;
            }
        }

        return null;
    }

    // Feature 1: Circuit Breaker helper - Enforces the 30s pause when threshold reached
    private Task WaitForCircuitBreakerAsync(Action<string>? logAction, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (now < _circuitBreakerOpenUntil)
        {
            var waitTime = _circuitBreakerOpenUntil - now;
            logAction?.Invoke($"[Circuit Breaker] Waiting {waitTime.TotalSeconds:F0}s to avoid hammering distressed server...");
            return Task.Delay(waitTime, cancellationToken);
        }

        return Task.CompletedTask;
    }

    private static readonly string SystemsCacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroGameCoverDownloader");
    internal static string SystemsCacheFilePath { get; set; } = Path.Combine(SystemsCacheDirectory, "systems_cache.json");

    internal async Task SaveSystemsToCacheAsync(List<SystemConfig> systems)
    {
        try
        {
            var cacheFile = SystemsCacheFilePath;
            var cacheDir = Path.GetDirectoryName(cacheFile);
            if (!string.IsNullOrEmpty(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            var json = JsonSerializer.Serialize(systems, _jsonOptions);
            await File.WriteAllTextAsync(cacheFile, json);
        }
        catch (Exception ex)
        {
            // Don't let cache write failures break the app
            _ = BugReportService.LogErrorAsync(ex, "[GitHubService] Failed to save systems cache.");
        }
    }

    internal async Task<List<SystemConfig>?> LoadSystemsFromCacheAsync()
    {
        try
        {
            var cacheFile = SystemsCacheFilePath;
            if (!File.Exists(cacheFile)) return null;

            var json = await File.ReadAllTextAsync(cacheFile);
            return JsonSerializer.Deserialize<List<SystemConfig>>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "[GitHubService] Failed to load systems cache.");
            return null;
        }
    }

    private static async Task<T> RetryOnTransientErrorAsync<T>(Func<Task<T>> action, int maxRetries = 3, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < maxRetries && IsTransientError(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 1.5);
                await Task.Delay(delay, cancellationToken);
            }
        }

        return await action();
    }

    private static bool IsTransientError(Exception ex)
    {
        if (ex is HttpRequestException httpEx)
        {
            // Don't retry on client errors (4xx) except 408 (Request Timeout) and 429 (Too Many Requests)
            if (httpEx.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
            {
                return httpEx.StatusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429;
            }

            return httpEx.InnerException is System.Net.Sockets.SocketException;
        }

        return ex is TaskCanceledException { InnerException: TimeoutException };
    }

    private static Dictionary<string, string> ParseGitmodules(string content)
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
            _ = BugReportService.LogErrorAsync(new InvalidOperationException("No systems were parsed from .gitmodules content."),
                $"{context}ParseGitmodules returned empty result.");
        }

        return map;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private bool _disposed;
}