using Serilog.Core;
using Serilog.Events;

namespace RetroGameCoverDownloader.Services;

/// <summary>
/// Serilog sink that forwards log events to the bug-report API.
/// Registered with restrictedToMinimumLevel: Warning, so it only ever
/// receives Warning, Error and Fatal events.
/// </summary>
public class BugReportSink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        // Fatal events are intentionally excluded: the global crash handlers in
        // App.xaml.cs report those *synchronously* (the process is terminating, so a
        // fire-and-forget async report might not finish), and reporting here too would
        // duplicate them. logEvent.Exception may be null for warnings; BugReportService
        // handles that.
        if (logEvent.Level is LogEventLevel.Warning or LogEventLevel.Error)
        {
            _ = BugReportService.LogErrorAsync(logEvent.Exception, logEvent.RenderMessage());
        }
    }
}
