using System.Runtime.CompilerServices;
using RetroGameCoverDownloader.Services;
using Serilog;

namespace RetroGameCoverDownloader.Tests;

internal static class TestModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        BugReportService.Current = new Services.MockBugReportService();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(new UiLogSink())
            .CreateLogger();
    }
}
