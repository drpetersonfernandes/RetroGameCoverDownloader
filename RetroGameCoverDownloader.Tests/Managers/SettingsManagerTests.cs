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

            Assert.DoesNotContain("ghp_secretToken123", rawText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("GitHubToken", rawText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void SaveSettingsAndLoadSettingsRoundTripsFileExtensions()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rgcd_test_settings_{Guid.NewGuid()}.dat");

        try
        {
            var originalSettings = new AppSettings
            {
                FileExtensions = [".nes", ".sfc", ".gba"]
            };

            SettingsManager.SaveSettings(originalSettings, tempPath);

            var loadedSettings = SettingsManager.LoadSettings(tempPath);

            Assert.Equal(originalSettings.FileExtensions, loadedSettings.FileExtensions);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void LoadSettingsWithCorruptFileReturnsDefaultSettings()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rgcd_test_settings_{Guid.NewGuid()}.dat");

        try
        {
            File.WriteAllBytes(tempPath, [0x01, 0x02, 0x03, 0x04, 0x05]);

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
    public void SaveSettingsProducesDifferentCiphertextEachTime()
    {
        var pathA = Path.Combine(Path.GetTempPath(), $"rgcd_test_settings_{Guid.NewGuid()}.dat");
        var pathB = Path.Combine(Path.GetTempPath(), $"rgcd_test_settings_{Guid.NewGuid()}.dat");

        try
        {
            var settings = new AppSettings { GitHubToken = "same_token" };

            SettingsManager.SaveSettings(settings, pathA);
            SettingsManager.SaveSettings(settings, pathB);

            var bytesA = File.ReadAllBytes(pathA);
            var bytesB = File.ReadAllBytes(pathB);

            Assert.NotEqual(bytesA, bytesB);

            // Both must still decrypt back to the same value
            Assert.Equal("same_token", SettingsManager.LoadSettings(pathA).GitHubToken);
            Assert.Equal("same_token", SettingsManager.LoadSettings(pathB).GitHubToken);
        }
        finally
        {
            if (File.Exists(pathA))
                File.Delete(pathA);
            if (File.Exists(pathB))
                File.Delete(pathB);
        }
    }

    [Fact]
    public void LoadSettingsWithExplicitPathDoesNotChangeDefaultSettingsFilePath()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rgcd_test_settings_{Guid.NewGuid()}.dat");

        try
        {
            SettingsManager.SaveSettings(new AppSettings(), tempPath);
            var defaultBefore = SettingsManager.DefaultSettingsFilePath;

            SettingsManager.LoadSettings(tempPath);

            Assert.Equal(defaultBefore, SettingsManager.DefaultSettingsFilePath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
