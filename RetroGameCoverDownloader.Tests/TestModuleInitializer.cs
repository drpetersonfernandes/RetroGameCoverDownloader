using System.Runtime.CompilerServices;
using RetroGameCoverDownloader.Services;

namespace RetroGameCoverDownloader.Tests;

internal static class TestModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        BugReportService.Current = new Services.MockBugReportService();
    }
}
