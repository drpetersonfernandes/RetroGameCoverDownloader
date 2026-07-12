using Serilog.Core;
using Serilog.Events;

namespace RetroGameCoverDownloader.Services;

public class UiLogSink : ILogEventSink
{
    public static event Action<string>? UiLogMessage;

    public void Emit(LogEvent logEvent)
    {
        var formatted = FormatLogEvent(logEvent);

        UiLogMessage?.Invoke(formatted);

        if (logEvent.Level >= LogEventLevel.Warning)
        {
            var message = logEvent.RenderMessage();
            var ex = logEvent.Exception ?? new Exception(message);
            _ = BugReportService.LogErrorAsync(ex, message);
        }
    }

    private static string FormatLogEvent(LogEvent logEvent)
    {
        var ts = logEvent.Timestamp.ToString("HH:mm:ss");
        var msg = logEvent.RenderMessage();
        return logEvent.Level switch
        {
            LogEventLevel.Error or LogEventLevel.Fatal => $"[{ts}] ERROR: {msg}",
            LogEventLevel.Warning => $"[{ts}] WARNING: {msg}",
            _ => $"[{ts}] {msg}"
        };
    }
}
