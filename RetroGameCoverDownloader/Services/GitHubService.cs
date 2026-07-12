using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using RetroGameCoverDownloader.Helpers;
using RetroGameCoverDownloader.Models;
using Serilog;

namespace RetroGameCoverDownloader.Services;

public class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;
    private readonly RateLimiter _rateLimiter;
    private readonly RetrySettings _retrySettings;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string MainRepoOwner = "libretro-thumbnails";
    private const string MainRepoName = "libretro-thumbnails";
    private static readonly char[] Separator = ['\r', '\n'];

    // Circuit breaker state (thread-safe counters for 503 tracking)
    private volatile int _consecutive503Count;
    private long _circuitBreakerOpenUntilTicks = DateTime.MinValue.Ticks;

    // 1. Expose the event wrapper
    public event Action<TimeSpan>? RateLimitHit
    {
        add => _rateLimiter.OnRateLimitHit += value;
        remove => _rateLimiter.OnRateLimitHit -= value;
    }

    public event Action? UnauthorizedAccess;

    // Ensures the UnauthorizedAccess event is raised at most once per service instance,
    // so a single failed operation that hits 401 on multiple branches does not prompt
    // the user (message box + token dialog) more than once. A new token creates a new
    // GitHubService, which naturally resets this guard.
    private int _unauthorizedNotified;

    private void RaiseUnauthorized()
    {
        if (Interlocked.Exchange(ref _unauthorizedNotified, 1) == 0)
        {
            UnauthorizedAccess?.Invoke();
        }
    }

    private static readonly string SystemsCacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroGameCoverDownloader");
    private static readonly string DefaultSystemsCacheFilePath = Path.Combine(SystemsCacheDirectory, "systems_cache.json");

    private readonly string _systemsCacheFilePath;

    internal GitHubService(HttpClient httpClient, RateLimiter? rateLimiter = null, string? systemsCacheFilePath = null, RetrySettings? retrySettings = null)
    {
        _httpClient = httpClient;
        _rateLimiter = rateLimiter ?? new RateLimiter(false);
        _systemsCacheFilePath = systemsCacheFilePath ?? DefaultSystemsCacheFilePath;
        _retrySettings = retrySettings ?? RetrySettings.Default;
    }

    public GitHubService(string? token, bool useProxy = false, string? proxyHost = null, int proxyPort = 0, string? proxyUsername = null, string? proxyPassword = null, string? systemsCacheFilePath = null, RetrySettings? retrySettings = null)
    {
        _rateLimiter = new RateLimiter(!string.IsNullOrWhiteSpace(token));
        _systemsCacheFilePath = systemsCacheFilePath ?? DefaultSystemsCacheFilePath;
        _retrySettings = retrySettings ?? RetrySettings.Default;

        if (useProxy && !string.IsNullOrWhiteSpace(proxyHost) && proxyPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(proxyPort), proxyPort, "Proxy port must be between 1 and 65535.");

        var handler = new HttpClientHandler();
        try
        {
            if (useProxy && !string.IsNullOrWhiteSpace(proxyHost) && proxyPort > 0)
            {
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

                if (!string.IsNullOrWhiteSpace(proxyUsername) && !string.IsNullOrWhiteSpace(proxyPassword))
                {
                    proxy.Credentials = new NetworkCredential(proxyUsername, proxyPassword);
                }

                handler.Proxy = proxy;
                handler.UseProxy = true;
            }

            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }
        catch (Exception ex)
        {
            handler.Dispose();
            Log.Error(ex, "Failed to initialize HttpClient in GitHubService.");
            throw new InvalidOperationException("Failed to initialize HttpClient in GitHubService.", ex);
        }

        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RetroGameCoverDownloader", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"));
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

    public async Task<List<SystemConfig>> GetAvailableSystemsAsync(CancellationToken cancellationToken = default)
    {
        const string context = "[GetAvailableSystemsAsync] ";

        var branches = new[] { "main", "master" };
        Exception? lastException = null;

        foreach (var branch in branches)
        {
            try
            {
                var systems = await TryFetchSystemsForBranchAsync(branch, cancellationToken);
                if (systems.Count > 0)
                {
                    await SaveSystemsToCacheAsync(systems);
                    return systems;
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                lastException = ex;
                var errorMsg = $"{context}GitHub API rate limit exceeded on branch '{branch}'. {ex.Message}";
                Log.Information(errorMsg);

                var cached = await LoadSystemsFromCacheAsync();
                if (cached != null)
                {
                    Log.Information("Using cached system list due to rate limiting.");
                    return cached;
                }

                Log.Information("No cached system list available. Trying next branch...");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                lastException = ex;
                Log.Information($"{context}GitHub API returned 401 (Unauthorized) on branch '{branch}'. Token may be missing, invalid, or expired.");
                RaiseUnauthorized();

                var cached = await LoadSystemsFromCacheAsync();
                if (cached != null)
                {
                    Log.Information("Using cached system list due to authentication error.");
                    return cached;
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                lastException = ex;
                Log.Information($"{context}Branch '{branch}' not found (404), trying next branch...");
            }
            catch (Exception ex)
            {
                lastException = ex;
                Log.Information($"{context}Error fetching systems on branch '{branch}': {ex.Message}");
            }
        }

        var fallbackCached = await LoadSystemsFromCacheAsync();
        if (fallbackCached != null)
        {
            Log.Information("Using cached system list due to error.");
            return fallbackCached;
        }

        if (lastException != null)
        {
            Log.Error(lastException, $"{context}Failed to fetch available systems from GitHub.");
        }

        return new List<SystemConfig>();
    }

    private async Task<List<SystemConfig>> TryFetchSystemsForBranchAsync(string branch, CancellationToken cancellationToken)
    {
        var systems = new List<SystemConfig>();

        Log.Information($"Fetching .gitmodules from branch '{branch}'...");
        var gitmodulesContent = await FetchGitmodulesAsync(branch, cancellationToken);
        var repoNameMap = ParseGitmodules(gitmodulesContent);

        Log.Information("Fetching main repository tree...");
        var mainRepoApiUrl = $"https://api.github.com/repos/{MainRepoOwner}/{MainRepoName}/git/trees/{branch}?recursive=1";

        await _rateLimiter.WaitForSlotAsync(cancellationToken);
        var jsonResponse = await RetryHelper.RetryOnTransientErrorAsync(() => _httpClient.GetStringAsync(mainRepoApiUrl, cancellationToken), _retrySettings, cancellationToken);
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

    private async Task<string> FetchGitmodulesAsync(string branch, CancellationToken cancellationToken)
    {
        var context = LogContext.ForMethod();
        var rawUrl = $"https://raw.githubusercontent.com/{MainRepoOwner}/{MainRepoName}/{branch}/.gitmodules";

        Exception? firstException;

        var rawRetrySettings = new RetrySettings { RetryOnForbidden = false };

        try
        {
            await _rateLimiter.WaitForSlotAsync(cancellationToken);
            return await RetryHelper.RetryOnTransientErrorAsync(() => _httpClient.GetStringAsync(rawUrl, cancellationToken), rawRetrySettings, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            firstException = ex;
            Log.Information($"{context}raw.githubusercontent.com rate limited ({ex.Message}), trying GitHub Contents API...");
        }
        catch (Exception ex)
        {
            firstException = ex;
            Log.Information($"{context}raw.githubusercontent.com failed ({ex.Message}), trying GitHub Contents API...");
        }

        try
        {
            var contentsApiUrl = $"https://api.github.com/repos/{MainRepoOwner}/{MainRepoName}/contents/.gitmodules?ref={branch}";
            await _rateLimiter.WaitForSlotAsync(cancellationToken);
            var json = await RetryHelper.RetryOnTransientErrorAsync(() => _httpClient.GetStringAsync(contentsApiUrl, cancellationToken), _retrySettings, cancellationToken);

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
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            var message = firstException is HttpRequestException { StatusCode: HttpStatusCode.Forbidden }
                ? $"{context}Both raw.githubusercontent.com and GitHub Contents API returned 403 (rate limit exceeded)."
                : $"{context}GitHub Contents API returned 403 (rate limit exceeded) after raw.githubusercontent.com fallback.";
            Log.Information(message);
            throw new InvalidOperationException(message, ex);
        }
    }

    public async Task<(string Branch, List<GitHubTreeItem> Files)> GetSystemFilesAsync(SystemConfig system, CancellationToken cancellationToken = default)
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

                using var response = await RetryHelper.RetryOnTransientErrorAsync(() => _httpClient.GetAsync(apiUrl, cancellationToken), _retrySettings, cancellationToken);

                switch (response.StatusCode)
                {
                    case HttpStatusCode.InternalServerError:
                        Log.Information($"{context}Repository too large for recursive fetch. Attempting non-recursive fallback...");
                        return await GetSystemFilesLargeRepoFallbackAsync(system, branch, cancellationToken);
                    case HttpStatusCode.Unauthorized:
                        Log.Information($"{context}GitHub API returned 401 (Unauthorized) on branch '{branch}'. Token may be missing, invalid, or expired.");
                        RaiseUnauthorized();
                        continue;
                    case HttpStatusCode.Forbidden:
                        Log.Information($"{context}Rate limit exceeded on branch '{branch}'. {response.ReasonPhrase}");
                        continue;
                    case HttpStatusCode.NotFound:
                        continue;
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
                Log.Error(ex, errorMsg);
            }
        }

        return (string.Empty, new List<GitHubTreeItem>());
    }

    /// <summary>
    /// Fallback for large repositories where recursive calls fail with 500 errors.
    /// Fetches the root, finds the target folder SHA, and fetches that folder's tree specifically.
    /// </summary>
    private async Task<(string Branch, List<GitHubTreeItem> Files)> GetSystemFilesLargeRepoFallbackAsync(SystemConfig system, string branch, CancellationToken cancellationToken = default)
    {
        var context = LogContext.ForMethod();
        try
        {
            // 1. Get root tree (non-recursive)
            var rootUrl = $"https://api.github.com/repos/{system.Owner}/{system.Repo}/git/trees/{branch}";
            await _rateLimiter.WaitForSlotAsync(cancellationToken);
            var rootJson = await RetryHelper.RetryOnTransientErrorAsync(() => _httpClient.GetStringAsync(rootUrl, cancellationToken), _retrySettings, cancellationToken);
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
                var folderJson = await RetryHelper.RetryOnTransientErrorAsync(() => _httpClient.GetStringAsync(folderUrl, cancellationToken), _retrySettings, cancellationToken);
                var folderTree = JsonSerializer.Deserialize<GitHubTree>(folderJson, _jsonOptions);

                if (folderTree?.Tree != null)
                {
                    // Map paths to include the folder prefix so the rest of the app logic remains compatible
                    var files = folderTree.Tree
                        .Where(static i => i.Type == "blob")
                        .Select(i => new GitHubTreeItem { Path = $"{system.FolderPath}/{i.Path}", Type = i.Type })
                        .ToList();

                    Log.Information($"{context}Successfully retrieved {files.Count} files via fallback method.");
                    return (branch, files);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GetSystemFilesLargeRepoFallbackAsync failed.");
        }

        return (string.Empty, new List<GitHubTreeItem>());
    }


    public async Task<byte[]?> DownloadFileAsync(string url, CancellationToken cancellationToken = default)
    {
        const string context = "[DownloadFileAsync] ";

        // Feature 1: Circuit Breaker - Check if we need to pause before attempting
        await WaitForCircuitBreakerAsync(cancellationToken);

        for (var attempt = 1; attempt <= _retrySettings.MaxRetries; attempt++)
        {
            try
            {
                await _rateLimiter.WaitForSlotAsync(cancellationToken);

                // Feature 2: User Feedback - Show current attempt
                Log.Information($"Downloading attempt {attempt}...");

                var data = await _httpClient.GetByteArrayAsync(url, cancellationToken);

                if (data.Length == 0)
                {
                    throw new InvalidOperationException($"Downloaded data is empty from URL: {url}");
                }

                // Success: Reset consecutive error count (closed circuit)
                Interlocked.Exchange(ref _consecutive503Count, 0);
                return data;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < _retrySettings.MaxRetries)
            {
                // Feature 1: Circuit Breaker Pattern - Track 503s
                var currentCount = Interlocked.Increment(ref _consecutive503Count);

                if (currentCount >= _retrySettings.CircuitBreakerThreshold)
                {
                    // Use CompareExchange to ensure only one thread triggers the cooldown
                    if (Interlocked.CompareExchange(ref _consecutive503Count, 0, currentCount) == currentCount)
                    {
                        Interlocked.Exchange(ref _circuitBreakerOpenUntilTicks, DateTime.UtcNow.AddSeconds(_retrySettings.CircuitBreakerCooldownSeconds).Ticks);
                        Log.Information($"{context}⚠️ Circuit breaker triggered: {_retrySettings.CircuitBreakerThreshold} consecutive 503s detected. Cooling down for {_retrySettings.CircuitBreakerCooldownSeconds}s...");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(_retrySettings.CircuitBreakerCooldownSeconds), cancellationToken);
                }
                else
                {
                    // Exponential backoff: 3s, 6s, 12s...
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * _retrySettings.BackoffMultiplierSeconds);

                    // Feature 2: User Feedback - Show retry status with 503 count
                    Log.Information($"{context}Server busy (503 attempt #{currentCount}). Retrying in {delay.TotalSeconds:F0}s...");
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException && attempt < _retrySettings.MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * _retrySettings.BackoffMultiplierSeconds);

                // Feature 2: User Feedback - Show timeout retry
                Log.Information($"{context}Download timeout. Retrying in {delay.TotalSeconds:F0}s...");
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Log final failure after all retries exhausted or non-transient error
                Log.Error(ex, $"{context}Failed on attempt {attempt} of {_retrySettings.MaxRetries}: {url}");
                return null;
            }
        }

        return null;
    }

    // Feature 1: Circuit Breaker helper - Enforces the 30s pause when threshold reached
    private Task WaitForCircuitBreakerAsync(CancellationToken cancellationToken)
    {
        var context = LogContext.ForMethod();
        var now = DateTime.UtcNow;
        var openUntil = new DateTime(Interlocked.Read(ref _circuitBreakerOpenUntilTicks), DateTimeKind.Utc);
        if (now < openUntil)
        {
            var waitTime = openUntil - now;
            Log.Information($"{context}Waiting {waitTime.TotalSeconds:F0}s to avoid hammering distressed server...");
            return Task.Delay(waitTime, cancellationToken);
        }

        return Task.CompletedTask;
    }

    internal async Task SaveSystemsToCacheAsync(List<SystemConfig> systems)
    {
        try
        {
            var cacheDir = Path.GetDirectoryName(_systemsCacheFilePath);
            if (!string.IsNullOrEmpty(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            var json = JsonSerializer.Serialize(systems, _jsonOptions);
            await File.WriteAllTextAsync(_systemsCacheFilePath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[GitHubService] Failed to save systems cache.");
        }
    }

    internal async Task<List<SystemConfig>?> LoadSystemsFromCacheAsync()
    {
        try
        {
            if (!File.Exists(_systemsCacheFilePath)) return null;

            var json = await File.ReadAllTextAsync(_systemsCacheFilePath);
            return JsonSerializer.Deserialize<List<SystemConfig>>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[GitHubService] Failed to load systems cache.");
            return null;
        }
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
                Log.Error(ex, $"{context}Exception parsing gitmodules line: {trimmed}");
                currentPath = null; // Reset to avoid corrupting next entry
            }
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