using System.Reflection;

namespace RetroGameCoverDownloader.Helpers;

public static class AppInfo
{
    private static readonly Version? VersionCache = Assembly.GetExecutingAssembly().GetName().Version;

    public static Version Version => VersionCache ?? new Version(0, 0, 0, 0);

    public static string VersionString => VersionCache?.ToString() ?? "Unknown";
}
