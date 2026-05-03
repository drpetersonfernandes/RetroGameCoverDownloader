using System.Xml.Serialization;

namespace RetroGameCoverDownloader.Models;

public class AppSettings
{
    public string? GitHubToken { get; set; }

    // Proxy settings for users in regions with network restrictions (e.g., China)
    public bool UseProxy { get; set; }
    public string? ProxyHost { get; set; }
    public int ProxyPort { get; set; }
    public string? ProxyUsername { get; set; }

    [XmlIgnore]
    public string? ProxyPassword { get; set; }

    [XmlElement("ProxyPassword")]
    public string? ProxyPasswordEncrypted { get; set; }

    public static string FormatProxyStatus(bool useProxy, string? proxyHost, int proxyPort)
    {
        return useProxy ? $"enabled (http://{proxyHost}:{proxyPort})" : "disabled";
    }
}