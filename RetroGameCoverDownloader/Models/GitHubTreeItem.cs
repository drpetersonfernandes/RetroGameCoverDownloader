namespace RetroGameCoverDownloader.Models;

public class GitHubTreeItem
{
    public string Path { get; init; } = "";
    public string Type { get; init; } = "";

    public string Mode { get; init; } = "";

    public string Sha { get; init; } = "";
}