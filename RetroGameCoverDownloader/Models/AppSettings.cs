namespace RetroGameCoverDownloader.Models;

public class AppSettings
{
    public string? GitHubToken { get; set; }

    public bool UseProxy { get; set; }
    public string? ProxyHost { get; set; }
    public int ProxyPort { get; set; }
    public string? ProxyUsername { get; set; }
    public string? ProxyPassword { get; set; }

    public List<string> FileExtensions { get; set; } = [..DefaultExtensions];

    public static string FormatProxyStatus(bool useProxy, string? proxyHost, int proxyPort)
    {
        return useProxy ? $"enabled (http://{proxyHost}:{proxyPort})" : "disabled";
    }

    public static readonly List<string> DefaultExtensions =
    [
        ".nes", ".sfc", ".smc", ".md", ".gen", ".gba", ".gb",
        ".gbc", ".n64", ".z64", ".v64", ".iso", ".cue", ".bin", ".img", ".ccd", ".chd",
        ".zip", ".7z", ".rar", ".rom", ".smd", ".gg", ".pce", ".lnx", ".ws",
        ".wsc", ".a78", ".a26", ".int", ".col"
    ];
}
