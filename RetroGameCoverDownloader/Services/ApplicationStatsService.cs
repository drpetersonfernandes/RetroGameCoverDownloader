using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RetroGameCoverDownloader.Helpers;
using Serilog;

namespace RetroGameCoverDownloader.Services;

public class ApplicationStatsService
{
    private const string StatsApiUrl = "https://www.purelogiccode.com/ApplicationStats/stats";
    private const string ApiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";
    private const string ApplicationId = "retro-game-cover-downloader";

    internal Func<HttpClient> HttpClientFactory { get; set; } = CreateDefaultHttpClient;

    internal static ApplicationStatsService Current { get; set; } = new();

    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private readonly object _httpClientLock = new();
    private HttpClient? _httpClientInstance;

    private HttpClient GetHttpClient()
    {
        lock (_httpClientLock)
        {
            return _httpClientInstance ??= HttpClientFactory();
        }
    }

    public static Task TrackLaunchAsync()
    {
        return Current.TrackLaunchCoreAsync();
    }

    private async Task TrackLaunchCoreAsync()
    {
        try
        {
            var version = AppInfo.VersionString;

            var payload = new
            {
                applicationId = ApplicationId,
                version
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, StatsApiUrl)
            {
                Content = httpContent
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

            using var response = await GetHttpClient().SendAsync(request);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to track launch telemetry.");
        }
    }
}
