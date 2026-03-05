namespace RetroGameCoverDownloader.Models;

public class AppSettings
{
    public string? GitHubToken { get; set; }

    // Proxy settings for users in regions with network restrictions (e.g., China)
    public bool UseProxy { get; set; }
    public string? ProxyHost { get; set; }
    public int ProxyPort { get; set; }
    public string? ProxyUsername { get; set; }
    public string? ProxyPassword { get; set; }
}