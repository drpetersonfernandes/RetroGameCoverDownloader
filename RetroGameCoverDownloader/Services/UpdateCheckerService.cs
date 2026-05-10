using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using RetroGameCoverDownloader.Helpers;
using RetroGameCoverDownloader.Models;

namespace RetroGameCoverDownloader.Services;

public static partial class UpdateCheckerService
{
    private const string RepoOwner = "drpetersonfernandes";
    private const string RepoName = "RetroGameCoverDownloader";
    private const string LatestApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static event Action<UpdateInfo>? UpdateAvailable;

    public static async Task CheckForUpdateAsync(Action<string>? logAction = null)
    {
        try
        {
            if (!Http.DefaultRequestHeaders.Contains("User-Agent"))
                Http.DefaultRequestHeaders.Add("User-Agent", $"{RepoName}-UpdateChecker");

            using var resp = await RetryHelper.RetryOnTransientErrorAsync(
                static () => Http.GetAsync(LatestApiUrl),
                logAction: logAction);

            if (!resp.IsSuccessStatusCode)
            {
                logAction?.Invoke($"Update check: GitHub API returned {(int)resp.StatusCode} ({resp.StatusCode}).");
                return;
            }

            await using var jsonStream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(jsonStream);

            var tagName = doc.RootElement.GetProperty("tag_name").GetString();
            var htmlUrl = doc.RootElement.GetProperty("html_url").GetString();
            if (tagName is null || htmlUrl is null)
            {
                logAction?.Invoke("Update check: Could not read release information from API response.");
                return;
            }

            var m = MyRegex().Match(tagName);
            if (!m.Success)
            {
                logAction?.Invoke($"Update check: Could not parse version from tag '{tagName}'.");
                return;
            }

            var latest = Version.Parse(m.Value);
            var current = AppInfo.Version;

            if (latest <= current)
            {
                logAction?.Invoke($"Update check: You are running the latest version ({current}).");
                return;
            }

            logAction?.Invoke($"Update available: {latest} (current: {current})");

            UpdateAvailable?.Invoke(new UpdateInfo
            {
                LatestVersion = latest,
                ReleaseUrl = htmlUrl
            });
        }
        catch (Exception ex)
        {
            logAction?.Invoke($"Update check failed: {ex.Message}");
            BugReportService.LogErrorSync(ex, "UpdateCheckerService.CheckForUpdateAsync");
        }
    }

    public static void OpenUrlInBrowser(string url)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            BugReportService.LogErrorSync(ex, $"UpdateCheckerService.OpenUrlInBrowser: {url}");
            System.Windows.MessageBox.Show(
                $"Could not launch browser automatically: {ex.Message}\n\nYou can open the page manually:\n{url}",
                "Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+(?:\.\d+)?")]
    private static partial Regex MyRegex();
}
