using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using RetroGameCoverDownloader.Models;
using RetroGameCoverDownloader.Services;

namespace RetroGameCoverDownloader.Managers;

public static class SettingsManager
{
    public static string DefaultSettingsFilePath { get; } = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "settings.xml");

    public static AppSettings LoadSettings()
    {
        return LoadSettings(DefaultSettingsFilePath);
    }

    public static AppSettings LoadSettings(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new AppSettings();
        }

        const string context = "[SettingsManager.LoadSettings] ";

        try
        {
            var serializer = new XmlSerializer(typeof(AppSettings));
            var xmlReaderSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using var reader = new StreamReader(filePath);
            using var xmlReader = XmlReader.Create(reader, xmlReaderSettings);
            var settings = (serializer.Deserialize(xmlReader) as AppSettings) ?? new AppSettings();

            if (!string.IsNullOrEmpty(settings.ProxyPasswordEncrypted))
            {
                try
                {
                    settings.ProxyPassword = DecryptString(settings.ProxyPasswordEncrypted);
                }
                catch (Exception ex) when (ex is CryptographicException or FormatException)
                {
                    settings.ProxyPassword = settings.ProxyPasswordEncrypted;
                }
            }

            if (!string.IsNullOrEmpty(settings.GitHubTokenEncrypted))
            {
                try
                {
                    settings.GitHubToken = DecryptString(settings.GitHubTokenEncrypted);
                }
                catch (Exception ex) when (ex is CryptographicException or FormatException)
                {
                    settings.GitHubToken = settings.GitHubTokenEncrypted;
                }
            }

            return settings;
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, $"{context}Failed to deserialize settings.xml. Creating new settings.");
            return new AppSettings();
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        SaveSettings(settings, DefaultSettingsFilePath);
    }

    public static void SaveSettings(AppSettings settings, string filePath)
    {
        const string context = "[SettingsManager.SaveSettings] ";
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var copy = new AppSettings
            {
                GitHubTokenEncrypted = string.IsNullOrEmpty(settings.GitHubToken)
                    ? null
                    : EncryptString(settings.GitHubToken),
                ProxyPasswordEncrypted = string.IsNullOrEmpty(settings.ProxyPassword)
                    ? null
                    : EncryptString(settings.ProxyPassword),
                UseProxy = settings.UseProxy,
                ProxyHost = settings.ProxyHost,
                ProxyPort = settings.ProxyPort,
                ProxyUsername = settings.ProxyUsername,
                FileExtensions = [..settings.FileExtensions]
            };

            var serializer = new XmlSerializer(typeof(AppSettings));
            using var writer = new StreamWriter(filePath);
            serializer.Serialize(writer, copy);
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, $"{context}Failed to save settings to {filePath}.");
            throw;
        }
    }

    private static string EncryptString(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string DecryptString(string encryptedText)
    {
        var bytes = Convert.FromBase64String(encryptedText);
        var unprotectedBytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(unprotectedBytes);
    }
}