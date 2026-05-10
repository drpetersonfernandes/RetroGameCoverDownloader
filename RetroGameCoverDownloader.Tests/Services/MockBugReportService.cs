using RetroGameCoverDownloader.Services;

namespace RetroGameCoverDownloader.Tests.Services;

internal class MockBugReportService : IBugReportService
{
    public List<(Exception? ex, string? context)> SyncCalls { get; } = new();
    public List<(Exception? ex, string? context)> AsyncCalls { get; } = new();

    public void LogErrorSync(Exception? ex, string? contextMessage = null)
    {
        SyncCalls.Add((ex, contextMessage));
    }

    public Task LogErrorAsync(Exception? ex, string? contextMessage = null)
    {
        AsyncCalls.Add((ex, contextMessage));
        return Task.CompletedTask;
    }
}
