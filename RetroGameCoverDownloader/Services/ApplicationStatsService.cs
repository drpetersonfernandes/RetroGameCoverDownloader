using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RetroGameCoverDownloader.Helpers;

namespace RetroGameCoverDownloader.Services;

public static class ApplicationStatsService
{
    private const string StatsApiUrl = "https://www.purelogiccode.com/ApplicationStats/stats";
    private const string ApiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";
    private const string ApplicationId = "retro-game-cover-downloader";

    private static readonly HttpClient HttpClientInstance;

    static ApplicationStatsService()
    {
        HttpClientInstance = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    /// <summary>
    /// Tracks application launch by sending usage statistics to the central ApplicationStats API.
    /// This method is fire-and-forget; failures are silently ignored.
    /// </summary>
    public static async Task TrackLaunchAsync()
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

            using var response = await HttpClientInstance.SendAsync(request);
            // Intentionally ignoring the response; fire-and-forget telemetry.
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "Failed to track launch telemetry.");
        }
    }
}
