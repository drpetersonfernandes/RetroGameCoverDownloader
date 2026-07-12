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
        var tempPath = Path.Combine(Path.GetTempPath(), $"rgcd_test_settings_{Guid.NewGuid()}.dat");

        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            var settings = SettingsManager.LoadSettings(tempPath);

            Assert.NotNull(settings);
            Assert.Null(settings.GitHubToken);
            Assert.False(settings.UseProxy);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void SaveSettingsAndLoadSettingsRoundTrip()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rgcd_test_settings_{Guid.NewGuid()}.dat");

        try
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

            SettingsManager.SaveSettings(originalSettings, tempPath);

            var loadedSettings = SettingsManager.LoadSettings(tempPath);

            Assert.Equal(originalSettings.GitHubToken, loadedSettings.GitHubToken);
            Assert.Equal(originalSettings.UseProxy, loadedSettings.UseProxy);
            Assert.Equal(originalSettings.ProxyHost, loadedSettings.ProxyHost);
            Assert.Equal(originalSettings.ProxyPort, loadedSettings.ProxyPort);
            Assert.Equal(originalSettings.ProxyUsername, loadedSettings.ProxyUsername);
            Assert.Equal(originalSettings.ProxyPassword, loadedSettings.ProxyPassword);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void SaveSettingsEncryptsFileContent()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rgcd_test_settings_{Guid.NewGuid()}.dat");

        try
        {
            var settings = new AppSettings
            {
                GitHubToken = "ghp_secretToken123",
                UseProxy = false
            };

            SettingsManager.SaveSettings(settings, tempPath);

            var rawBytes = File.ReadAllBytes(tempPath);
            var rawText = System.Text.Encoding.UTF8.GetString(rawBytes);

            Assert.DoesNotContain("ghp_secretToken123", rawText);
            Assert.DoesNotContain("GitHubToken", rawText);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
