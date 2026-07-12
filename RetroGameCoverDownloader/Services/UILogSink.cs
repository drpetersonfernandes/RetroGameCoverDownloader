using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace RetroGameCoverDownloader.Services;

public class UiLogSink : ILogEventSink
{
    private static readonly ConcurrentQueue<string> Buffer = new();
    private static volatile Action<string>? _uiHandler;

    public static void SetUiHandler(Action<string>? handler)
    {
        _uiHandler = handler;

        if (handler != null)
        {
            while (Buffer.TryDequeue(out var msg))
                handler(msg);
        }
    }

    public void Emit(LogEvent logEvent)
    {
        var formatted = FormatLogEvent(logEvent);

        var handler = _uiHandler;
        if (handler != null)
        {
            handler(formatted);
        }
        else
        {
            Buffer.Enqueue(formatted);
        }

        if (logEvent.Level >= LogEventLevel.Error)
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
