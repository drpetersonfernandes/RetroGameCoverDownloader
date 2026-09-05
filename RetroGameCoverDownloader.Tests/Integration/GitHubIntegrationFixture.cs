using RetroGameCoverDownloader.Models;
using RetroGameCoverDownloader.Services;

namespace RetroGameCoverDownloader.Tests.Integration;

/// <summary>
/// Shared fixture for all GitHub integration tests.
/// Fetches the full list of libretro-thumbnails systems once per test run.
/// </summary>
public static class GitHubIntegrationFixture
{
    public static IReadOnlyList<SystemConfig> Systems { get; }
    public static string? FetchError { get; }
    public static GitHubService SharedService { get; }

    static GitHubIntegrationFixture()
    {
        try
        {
            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            SharedService = new GitHubService(token);
            Systems = SharedService.GetAvailableSystemsAsync().GetAwaiter().GetResult();
            if (Systems.Count == 0)
            {
                FetchError = "GetAvailableSystemsAsync returned an empty list.";
            }
        }
        catch (Exception ex)
        {
            FetchError = $"Exception: {ex.GetType().Name}: {ex.Message}";
            Systems = new List<SystemConfig>();
            SharedService = new GitHubService((string?)null);
        }
    }

    public static IEnumerable<object[]> GetSystems()
    {
        if (Systems.Count == 0)
        {
            yield return [new SystemConfig("SKIP", "skip", "skip", "skip"), true];

            yield break;
        }

        foreach (var system in Systems)
        {
            yield return [system, false];
        }
    }
}