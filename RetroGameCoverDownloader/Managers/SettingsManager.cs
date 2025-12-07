using System.IO;
using System.Xml;
using System.Xml.Serialization;
using RetroGameCoverDownloader.Models;
using RetroGameCoverDownloader.Services;

namespace RetroGameCoverDownloader.Managers;

public static class SettingsManager
{
    private static readonly string SettingsFilePath = Path.Combine(AppContext.BaseDirectory, "settings.xml");

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
            return (serializer.Deserialize(xmlReader) as AppSettings) ?? new AppSettings(); // Deserialize using the secure XmlReader
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
            var serializer = new XmlSerializer(typeof(AppSettings));
            using var writer = new StreamWriter(SettingsFilePath);
            serializer.Serialize(writer, settings);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{context}Error: Could not save settings to {SettingsFilePath}. Error: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, $"{context}Failed to save settings to {SettingsFilePath}.");
        }
    }
}