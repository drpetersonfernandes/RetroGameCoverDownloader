namespace RetroGameCoverDownloader.Models;

public sealed class UpdateInfo
{
    public required Version LatestVersion { get; init; }
    public required string ReleaseUrl { get; init; }
}
