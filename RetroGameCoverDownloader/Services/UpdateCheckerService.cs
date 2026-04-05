using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace RetroGameCoverDownloader.Services;

public static partial class UpdateCheckerService
{
    private const string RepoOwner = "drpetersonfernandes";
    private const string RepoName = "CSharp_RetroGameCoverDownloader";
    private const string LatestApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static async Task CheckForUpdateAsync()
    {
        try
        {
            // GitHub rejects requests without a User-Agent header.
            if (!Http.DefaultRequestHeaders.Contains("User-Agent"))
                Http.DefaultRequestHeaders.Add("User-Agent", $"{RepoName}-UpdateChecker");

            using var resp = await RetryOnTransientErrorAsync(static () => Http.GetAsync(LatestApiUrl));
            if (!resp.IsSuccessStatusCode) return; // silent if offline or GitHub unhappy

            await using var jsonStream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(jsonStream);

            var tagName = doc.RootElement.GetProperty("tag_name").GetString();
            var htmlUrl = doc.RootElement.GetProperty("html_url").GetString();
            if (tagName is null || htmlUrl is null) return;

            // We expect tags like "v1.0.2" or "release_1.0.2" – pick the 1.0.2 part.
            var m = MyRegex().Match(tagName);
            if (!m.Success) return;

            var latest = Version.Parse(m.Value);
            var current = Assembly.GetExecutingAssembly().GetName().Version
                          ?? new Version(0, 0, 0, 0);

            if (latest <= current) return; // up-to-date

            var message = new StringBuilder();
            message.AppendLine("A newer version of Retro Game Cover Downloader is available:");
            message.AppendLine();
            message.AppendLine(CultureInfo.InvariantCulture, $"  Current Version: {current}");
            message.AppendLine(CultureInfo.InvariantCulture, $"  Latest Version : {latest}");
            message.AppendLine();
            message.Append("Would you like to open the release page in your browser?");

            var result = MessageBox.Show(message.ToString(), "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName = htmlUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not launch browser automatically: {ex.Message}\n\nYou can open the page manually:\n{htmlUrl}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: log and continue silently
            BugReportService.LogErrorSync(ex, "UpdateCheckerService.CheckForUpdateAsync");
        }
    }

    private static async Task<T> RetryOnTransientErrorAsync<T>(Func<Task<T>> action, int maxRetries = 3)
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
                await Task.Delay(delay);
            }
        }

        return await action();
    }

    private static bool IsTransientError(Exception ex)
    {
        return ex is TaskCanceledException { InnerException: TimeoutException }
            or HttpRequestException { InnerException: System.Net.Sockets.SocketException };
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex MyRegex();
}
