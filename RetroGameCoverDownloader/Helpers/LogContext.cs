using System.IO;
using System.Runtime.CompilerServices;

namespace RetroGameCoverDownloader.Helpers;

internal static class LogContext
{
    public static string ForMethod(
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = "")
    {
        var className = Path.GetFileNameWithoutExtension(callerFilePath);
        if (callerMemberName is ".ctor" or ".cctor")
        {
            return $"[{className}] ";
        }

        return $"[{className}.{callerMemberName}] ";
    }
}
