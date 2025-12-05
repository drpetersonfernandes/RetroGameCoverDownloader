namespace RetroGameCoverDownloader.Models;

public class GitHubTree
{
    public string Sha { get; set; } = "";
    public string Url { get; set; } = "";
    public List<GitHubTreeItem> Tree { get; set; } = new();
    public bool Truncated { get; set; }
}