using Serilog.Core;
using Serilog.Events;

namespace RetroGameCoverDownloader.Services;

public class UiLogSink : ILogEventSink
{
    private const int MaxBufferedMessages = 1000;

    // A single gate guards both the buffer and the handler reference so that
    // "attach handler + drain buffer" and "read handler / enqueue" are atomic
    // with respect to each other. Without this, a message emitted on another
    // thread in the instant between the null-check and enqueue could be stranded
    // in the buffer until the next SetUiHandler call.
    private static readonly Lock Gate = new();
    private static readonly Queue<string> Buffer = new();
    private static Action<string>? _uiHandler;

    public static void SetUiHandler(Action<string>? handler)
    {
        string[]? pending = null;

        lock (Gate)
        {
            _uiHandler = handler;

            if (handler != null && Buffer.Count > 0)
            {
                pending = Buffer.ToArray();
                Buffer.Clear();
            }
        }

        // Replay outside the lock: the handler marshals to the UI thread, which
        // may itself log, so holding the gate here could deadlock.
        if (pending != null)
        {
            foreach (var msg in pending)
                handler!(msg);
        }
    }

    public void Emit(LogEvent logEvent)
    {
        var formatted = FormatLogEvent(logEvent);

        Action<string>? handler;

        lock (Gate)
        {
            handler = _uiHandler;

            if (handler == null)
            {
                Buffer.Enqueue(formatted);

                // Cap the buffer so log messages can't accumulate unbounded
                // when no UI handler is ever attached (e.g. headless/test hosts).
                while (Buffer.Count > MaxBufferedMessages)
                    Buffer.Dequeue();

                return;
            }
        }

        // Invoke outside the lock to avoid holding it during UI dispatch.
        handler(formatted);
    }

    private static string FormatLogEvent(LogEvent logEvent)
    {
        var ts = logEvent.Timestamp.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var msg = logEvent.RenderMessage();
        return logEvent.Level switch
        {
            LogEventLevel.Error or LogEventLevel.Fatal => $"[{ts}] ERROR: {msg}",
            LogEventLevel.Warning => $"[{ts}] WARNING: {msg}",
            _ => $"[{ts}] {msg}"
        };
    }
}
