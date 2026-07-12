using RetroGameCoverDownloader.Models;

namespace RetroGameCoverDownloader.Services;

public interface IGitHubService : IDisposable
{
    event Action<TimeSpan>? RateLimitHit;
    event Action? UnauthorizedAccess;

    Task<List<SystemConfig>> GetAvailableSystemsAsync(CancellationToken cancellationToken = default);

    Task<(string Branch, List<GitHubTreeItem> Files)> GetSystemFilesAsync(SystemConfig system, CancellationToken cancellationToken = default);

    Task<byte[]?> DownloadFileAsync(string url, CancellationToken cancellationToken = default);
}
