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
}
