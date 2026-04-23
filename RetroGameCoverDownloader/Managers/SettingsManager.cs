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
    internal static string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RetroGameCoverDownloader",
        "settings.xml");

    public static AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new AppSettings();
        }

        const string context = "[SettingsManager.LoadSettings] ";

        try
        {
            var serializer = new XmlSerializer(typeof(AppSettings));
            // Create XmlReaderSettings to disable DTD processing and XmlResolver for security
            var xmlReaderSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using var reader = new StreamReader(SettingsFilePath); // Use StreamReader to read file content
            using var xmlReader = XmlReader.Create(reader, xmlReaderSettings); // Create XmlReader with secure settings
            var settings = (serializer.Deserialize(xmlReader) as AppSettings) ?? new AppSettings(); // Deserialize using the secure XmlReader

            // Decrypt proxy password if present
            if (!string.IsNullOrEmpty(settings.ProxyPasswordEncrypted))
            {
                try
                {
                    settings.ProxyPassword = DecryptString(settings.ProxyPasswordEncrypted);
                }
                catch (Exception ex) when (ex is CryptographicException or FormatException)
                {
                    // Value may be an old plain-text password; migrate transparently
                    settings.ProxyPassword = settings.ProxyPasswordEncrypted;
                }
            }

            return settings;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{context}Warning: Could not read settings file. A new one will be created. Error: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, $"{context}Failed to deserialize settings.xml. Creating new settings.");
            return new AppSettings();
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        const string context = "[SettingsManager.SaveSettings] ";
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Encrypt proxy password before saving
            if (!string.IsNullOrEmpty(settings.ProxyPassword))
            {
                settings.ProxyPasswordEncrypted = EncryptString(settings.ProxyPassword);
            }
            else
            {
                settings.ProxyPasswordEncrypted = null;
            }

            var serializer = new XmlSerializer(typeof(AppSettings));
            using var writer = new StreamWriter(SettingsFilePath);
            serializer.Serialize(writer, settings);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{context}Error: Could not save settings to {SettingsFilePath}. Error: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, $"{context}Failed to save settings to {SettingsFilePath}.");
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