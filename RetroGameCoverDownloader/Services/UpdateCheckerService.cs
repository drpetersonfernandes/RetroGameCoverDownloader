using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using RetroGameCoverDownloader.Helpers;
using RetroGameCoverDownloader.Models;
using Serilog;

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

    public static async Task CheckForUpdateAsync()
    {
        try
        {
            if (!Http.DefaultRequestHeaders.Contains("User-Agent"))
                Http.DefaultRequestHeaders.Add("User-Agent", $"{RepoName}-UpdateChecker");

            using var resp = await RetryHelper.RetryOnTransientErrorAsync(static () => Http.GetAsync(LatestApiUrl));

            if (!resp.IsSuccessStatusCode)
            {
                Log.Information("Update check: GitHub API returned {StatusCode} ({Status}).", (int)resp.StatusCode, resp.StatusCode);
                return;
            }

            await using var jsonStream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(jsonStream);

            var tagName = doc.RootElement.GetProperty("tag_name").GetString();
            var htmlUrl = doc.RootElement.GetProperty("html_url").GetString();
            if (tagName is null || htmlUrl is null)
            {
                Log.Information("Update check: Could not read release information from API response.");
                return;
            }

            var m = MyRegex().Match(tagName);
            if (!m.Success)
            {
                Log.Information("Update check: Could not parse version from tag '{TagName}'.", tagName);
                return;
            }

            var latest = Version.Parse(m.Value);
            var current = AppInfo.Version;

            if (latest <= current)
            {
                Log.Information("Update check: You are running the latest version ({Current}).", current);
                return;
            }

            Log.Information("Update available: {Latest} (current: {Current})", latest, current);

            UpdateAvailable?.Invoke(new UpdateInfo
            {
                LatestVersion = latest,
                ReleaseUrl = htmlUrl
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update checker failed.");
        }
    }

    public static void OpenUrlInBrowser(string url)
    {
        if (TryOpenWithShellExecute(url)) return;
        if (TryOpenWithCmdStart(url)) return;

        try { System.Windows.Clipboard.SetText(url); }
        catch
        {
            // ignored
        }

        Log.Error("All browser-launch strategies failed for URL: {Url}", url);

        System.Windows.MessageBox.Show(
            $"Could not launch the browser automatically.\n\nThe URL has been copied to your clipboard:\n\n{url}",
            "Error",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
    }

    private static bool TryOpenWithShellExecute(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    private static bool TryOpenWithCmdStart(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" \"{url}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return true;
        }
        catch { return false; }
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+(?:\.\d+)?")]
    private static partial Regex MyRegex();
}
