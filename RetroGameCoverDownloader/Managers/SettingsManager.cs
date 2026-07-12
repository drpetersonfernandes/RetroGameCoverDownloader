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

    private static readonly string AppFolderPath = Path.Combine(
        AppContext.BaseDirectory, SettingsFileName);

    private static readonly string AppDataPath = Path.Combine(
        AppInfo.LocalAppDataFolderPath, SettingsFileName);

    private static string? _loadedFromPath;

    private static readonly byte[] Key = DeriveAesKey();

    public static string DefaultSettingsFilePath => _loadedFromPath ?? AppFolderPath;

    public static AppSettings LoadSettings()
    {
        var candidatePaths = new[] { AppFolderPath, AppDataPath };
        var existing = candidatePaths.Where(File.Exists).ToList();

        if (existing.Count > 0)
        {
            var newest = existing.MaxBy(File.GetLastWriteTimeUtc)!;
            _loadedFromPath = newest;
            return LoadFromDat(newest);
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
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var encrypted = EncryptBytes(plainBytes);

        File.WriteAllBytes(filePath, encrypted);
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

    private static byte[] DeriveAesKey()
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            "RetroGameCoverDownloader.Settings.Encryption.v2"u8.ToArray(),
            "RGCD_SALT_2026"u8.ToArray(),
            100_000,
            HashAlgorithmName.SHA256,
            32);
    }

    private static byte[] EncryptBytes(byte[] plainBytes)
    {
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);

        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cs.Write(plainBytes, 0, plainBytes.Length);
            cs.FlushFinalBlock();
        }

        return ms.ToArray();
    }

    private static byte[] DecryptBytes(byte[] encryptedBytes)
    {
        using var aes = Aes.Create();
        aes.Key = Key;

        var iv = new byte[aes.BlockSize / 8];
        Array.Copy(encryptedBytes, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var ms = new MemoryStream(encryptedBytes, iv.Length, encryptedBytes.Length - iv.Length);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var result = new MemoryStream();
        cs.CopyTo(result);
        return result.ToArray();
    }
}
