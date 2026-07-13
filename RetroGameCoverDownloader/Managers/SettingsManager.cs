using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using RetroGameCoverDownloader.Helpers;
using RetroGameCoverDownloader.Models;
using Serilog;

namespace RetroGameCoverDownloader.Managers;

public static class SettingsManager
{
    private const string SettingsFileName = "settings.dat";
    private const string LegacySettingsFileName = "settings.xml";

    private static readonly string AppDataPath = Path.Combine(
        AppInfo.LocalAppDataFolderPath, SettingsFileName);

    private static string? _loadedFromPath;

    // Optional entropy mixed into DPAPI. This is not a secret key (DPAPI derives the
    // real key from the current Windows user's credentials); it just ties the blob to
    // this application so it can't be trivially decrypted by unrelated CurrentUser code.
    private static readonly byte[] Entropy = "RetroGameCoverDownloader.Settings.v2"u8.ToArray();

    // When nothing has been loaded yet, default to the (writable) per-user AppData
    // location. This must match the fallback used by SaveSettings() so that
    // DefaultSettingsFilePath and the actual save target stay consistent.
    public static string DefaultSettingsFilePath => _loadedFromPath ?? AppDataPath;

    public static AppSettings LoadSettings()
    {
        if (File.Exists(AppDataPath))
        {
            _loadedFromPath = AppDataPath;
            return LoadFromDat(AppDataPath);
        }

        var legacyPath = Path.Combine(AppContext.BaseDirectory, LegacySettingsFileName);
        if (File.Exists(legacyPath))
        {
            var migrated = MigrateFromLegacyXml(legacyPath);
            return migrated;
        }

        _loadedFromPath = null;
        return new AppSettings();
    }

    public static AppSettings LoadSettings(string filePath)
    {
        if (!File.Exists(filePath))
            return new AppSettings();

        return LoadFromDat(filePath);
    }

    public static void SaveSettings(AppSettings settings)
    {
        var path = _loadedFromPath ?? AppDataPath;
        SaveSettings(settings, path);
    }

    public static void SaveSettings(AppSettings settings, string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(settings);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var encrypted = EncryptBytes(plainBytes);

            File.WriteAllBytes(filePath, encrypted);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"[SettingsManager] Failed to save settings to {filePath}.");
            throw;
        }
    }

    private static AppSettings LoadFromDat(string filePath)
    {
        try
        {
            var encrypted = File.ReadAllBytes(filePath);
            var plainBytes = DecryptBytes(encrypted);
            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (CryptographicException ex)
        {
            Log.Error(ex, "[SettingsManager] Settings file is corrupt or was encrypted on a different machine. Creating new settings.");
            return new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"[SettingsManager] Failed to load {filePath}. Creating new settings.");
            return new AppSettings();
        }
    }

    private static AppSettings MigrateFromLegacyXml(string legacyPath)
    {
        try
        {
            var settings = LegacyXmlParser(legacyPath);

            // Save to new format at the app data path
            var datPath = AppDataPath;
            var json = JsonSerializer.Serialize(settings);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var encrypted = EncryptBytes(plainBytes);

            var directory = Path.GetDirectoryName(datPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(datPath, encrypted);
            _loadedFromPath = datPath;

            return settings;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SettingsManager] Failed to migrate legacy settings.xml. Creating new settings.");
            return new AppSettings();
        }
    }

    private static AppSettings LegacyXmlParser(string filePath)
    {
        var settings = new AppSettings();

        try
        {
            var doc = new XmlDocument();
            doc.Load(filePath);

            var tokenNode = doc.SelectSingleNode("/AppSettings/GitHubToken");
            if (tokenNode != null && !string.IsNullOrEmpty(tokenNode.InnerText))
            {
                try
                {
                    settings.GitHubToken = DecryptLegacyString(tokenNode.InnerText);
                }
                catch (Exception ex) when (ex is CryptographicException or FormatException)
                {
                    settings.GitHubToken = tokenNode.InnerText;
                }
            }

            var useProxyNode = doc.SelectSingleNode("/AppSettings/UseProxy");
            settings.UseProxy = useProxyNode != null && bool.TryParse(useProxyNode.InnerText, out var up) && up;

            var hostNode = doc.SelectSingleNode("/AppSettings/ProxyHost");
            settings.ProxyHost = hostNode?.InnerText;

            var portNode = doc.SelectSingleNode("/AppSettings/ProxyPort");
            if (portNode != null && int.TryParse(portNode.InnerText, out var port))
            {
                settings.ProxyPort = port;
            }

            var userNode = doc.SelectSingleNode("/AppSettings/ProxyUsername");
            settings.ProxyUsername = userNode?.InnerText;

            var passNode = doc.SelectSingleNode("/AppSettings/ProxyPassword");
            if (passNode != null && !string.IsNullOrEmpty(passNode.InnerText))
            {
                try
                {
                    settings.ProxyPassword = DecryptLegacyString(passNode.InnerText);
                }
                catch (Exception ex) when (ex is CryptographicException or FormatException)
                {
                    settings.ProxyPassword = passNode.InnerText;
                }
            }

            var extNodes = doc.SelectNodes("/AppSettings/FileExtensions/Extension");
            if (extNodes != null)
            {
                settings.FileExtensions.Clear();
                foreach (XmlNode node in extNodes)
                {
                    if (!string.IsNullOrWhiteSpace(node.InnerText))
                        settings.FileExtensions.Add(node.InnerText.Trim());
                }

                if (settings.FileExtensions.Count == 0)
                {
                    settings.FileExtensions = [..AppSettings.DefaultExtensions];
                }
            }
        }
        catch
        {
            return new AppSettings();
        }

        return settings;
    }

    private static string DecryptLegacyString(string encryptedText)
    {
        var bytes = Convert.FromBase64String(encryptedText);
        var unprotected = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(unprotected);
    }

    // Protect the settings blob with Windows DPAPI (CurrentUser scope). The encryption
    // key is derived from the logged-in user's credentials by the OS and never leaves
    // the machine, so the file cannot be decrypted by another user or on another PC.
    private static byte[] EncryptBytes(byte[] plainBytes)
    {
        return ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
    }

    private static byte[] DecryptBytes(byte[] encryptedBytes)
    {
        return ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
    }
}
