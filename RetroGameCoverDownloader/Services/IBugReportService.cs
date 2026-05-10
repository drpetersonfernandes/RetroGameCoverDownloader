namespace RetroGameCoverDownloader.Services;

public interface IBugReportService
{
    void LogErrorSync(Exception? ex, string? contextMessage = null);
    Task LogErrorAsync(Exception? ex, string? contextMessage = null);
}
