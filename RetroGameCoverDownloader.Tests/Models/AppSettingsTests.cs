using RetroGameCoverDownloader.Models;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void AppSettings_DefaultValues_AreNullOrFalse()
    {
        var settings = new AppSettings();

        Assert.Null(settings.GitHubToken);
        Assert.False(settings.UseProxy);
        Assert.Null(settings.ProxyHost);
        Assert.Equal(0, settings.ProxyPort);
        Assert.Null(settings.ProxyUsername);
        Assert.Null(settings.ProxyPassword);
    }

    [Fact]
    public void AppSettings_CanSetProperties()
    {
        var settings = new AppSettings
        {
            GitHubToken = "ghp_testtoken",
            UseProxy = true,
            ProxyHost = "proxy.example.com",
            ProxyPort = 8080,
            ProxyUsername = "user",
            ProxyPassword = "pass"
        };

        Assert.Equal("ghp_testtoken", settings.GitHubToken);
        Assert.True(settings.UseProxy);
        Assert.Equal("proxy.example.com", settings.ProxyHost);
        Assert.Equal(8080, settings.ProxyPort);
        Assert.Equal("user", settings.ProxyUsername);
        Assert.Equal("pass", settings.ProxyPassword);
    }

    [Theory]
    [InlineData(false, null, 0, "disabled")]
    [InlineData(true, "proxy.example.com", 8080, "enabled (http://proxy.example.com:8080)")]
    [InlineData(true, "127.0.0.1", 1080, "enabled (http://127.0.0.1:1080)")]
    public void FormatProxyStatus_ReturnsExpectedString(bool useProxy, string? host, int port, string expected)
    {
        var result = AppSettings.FormatProxyStatus(useProxy, host, port);
        Assert.Equal(expected, result);
    }
}
