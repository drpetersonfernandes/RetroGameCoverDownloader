using RetroGameCoverDownloader.Models;

namespace RetroGameCoverDownloader.Services;

public interface IGitHubService : IDisposable
{
    event Action<TimeSpan>? RateLimitHit;

    Task<List<SystemConfig>> GetAvailableSystemsAsync(Action<string> logAction, CancellationToken cancellationToken = default);

    Task<(string Branch, List<GitHubTreeItem> Files)> GetSystemFilesAsync(SystemConfig system, Action<string> logAction, CancellationToken cancellationToken = default);

    Task<byte[]?> DownloadFileAsync(string url, Action<string>? logAction = null, CancellationToken cancellationToken = default);
}
