using System.IO;
using Serilog;

namespace RetroGameCoverDownloader.Services;

public static class LogConfig
{
    public static void Initialize()
    {
        var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logDir, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Debug(
                outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(new UiLogSink())
            .CreateLogger();
    }
}
