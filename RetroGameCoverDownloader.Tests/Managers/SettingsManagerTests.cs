using System.IO;
using RetroGameCoverDownloader.Managers;
using RetroGameCoverDownloader.Models;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Managers;

public class SettingsManagerTests
{
    [Fact]
    public void LoadSettings_FileDoesNotExist_ReturnsDefaultSettings()
    {
        var settings = SettingsManager.LoadSettings();

        Assert.NotNull(settings);
        Assert.Null(settings.GitHubToken);
        Assert.False(settings.UseProxy);
    }

    [Fact]
    public void SaveSettings_AndLoadSettings_RoundTrip()
    {
        var originalSettings = new AppSettings
        {
            GitHubToken = "test_token_123",
            UseProxy = true,
            ProxyHost = "localhost",
            ProxyPort = 8080,
            ProxyUsername = "user",
            ProxyPassword = "secret"
        };

        SettingsManager.SaveSettings(originalSettings);

        var loadedSettings = SettingsManager.LoadSettings();

        Assert.Equal(originalSettings.GitHubToken, loadedSettings.GitHubToken);
        Assert.Equal(originalSettings.UseProxy, loadedSettings.UseProxy);
        Assert.Equal(originalSettings.ProxyHost, loadedSettings.ProxyHost);
        Assert.Equal(originalSettings.ProxyPort, loadedSettings.ProxyPort);
        Assert.Equal(originalSettings.ProxyUsername, loadedSettings.ProxyUsername);
        Assert.Equal(originalSettings.ProxyPassword, loadedSettings.ProxyPassword);

        // Cleanup
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.xml");
        if (File.Exists(settingsPath))
        {
            File.Delete(settingsPath);
        }
    }
}
