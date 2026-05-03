using System.IO;
using RetroGameCoverDownloader.Managers;
using RetroGameCoverDownloader.Models;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Managers;

public class SettingsManagerTests
{
    [Fact]
    public void LoadSettingsFileDoesNotExistReturnsDefaultSettings()
    {
        var originalPath = SettingsManager.SettingsFilePath;
        var tempPath = Path.Combine(Path.GetTempPath(), $"rgcd_test_settings_{Guid.NewGuid()}.xml");

        try
        {
            SettingsManager.SettingsFilePath = tempPath;
            // Ensure the file does not exist
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            var settings = SettingsManager.LoadSettings();

            Assert.NotNull(settings);
            Assert.Null(settings.GitHubToken);
            Assert.False(settings.UseProxy);
        }
        finally
        {
            SettingsManager.SettingsFilePath = originalPath;
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void SaveSettingsAndLoadSettingsRoundTrip()
    {
        var originalPath = SettingsManager.SettingsFilePath;
        var tempPath = Path.Combine(Path.GetTempPath(), $"rgcd_test_settings_{Guid.NewGuid()}.xml");

        try
        {
            SettingsManager.SettingsFilePath = tempPath;

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
        }
        finally
        {
            SettingsManager.SettingsFilePath = originalPath;
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
