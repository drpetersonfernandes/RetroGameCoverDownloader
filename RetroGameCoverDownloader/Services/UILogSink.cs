using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace RetroGameCoverDownloader.Services;

public class UiLogSink : ILogEventSink
{
    private const int MaxBufferedMessages = 1000;

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

            // Cap the buffer so log messages can't accumulate unbounded
            // when no UI handler is ever attached (e.g. headless/test hosts).
            while (Buffer.Count > MaxBufferedMessages && Buffer.TryDequeue(out _))
            {
            }
        }

        // Auto-report genuine errors that carry an exception. Fatal-level events
        // are intentionally excluded: the global crash handlers in App.xaml.cs
        // report those synchronously, so reporting here too would duplicate them.
        if (logEvent is { Level: LogEventLevel.Error, Exception: not null })
        {
            _ = BugReportService.LogErrorAsync(logEvent.Exception, logEvent.RenderMessage());
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
