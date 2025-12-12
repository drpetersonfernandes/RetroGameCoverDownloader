namespace RetroGameCoverDownloader.Services;

public class RateLimiter
{
    private int _maxRequests;
    private readonly TimeSpan _timeWindow;
    private readonly Queue<DateTime> _requestTimestamps = new();
    private readonly object _lock = new();

    // 1. Add an event to notify listeners (ViewModel)
    public event Action<TimeSpan>? OnRateLimitHit;

    public RateLimiter(bool isAuthenticated)
    {
        UpdateLimit(isAuthenticated);
        _timeWindow = TimeSpan.FromHours(1);
    }

    public void UpdateLimit(bool isAuthenticated)
    {
        lock (_lock)
        {
            _maxRequests = isAuthenticated ? 4900 : 55;
        }
    }

    public async Task WaitForSlotAsync()
    {
        const string context = "[RateLimiter.WaitForSlotAsync] ";

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            while (_requestTimestamps.Count > 0 && (now - _requestTimestamps.Peek()) > _timeWindow)
            {
                _requestTimestamps.Dequeue();
            }
        }

        TimeSpan timeToWait;
        lock (_lock)
        {
            if (_requestTimestamps.Count < _maxRequests)
            {
                _requestTimestamps.Enqueue(DateTime.UtcNow);
                return;
            }

            var oldestRequest = _requestTimestamps.Peek();
            timeToWait = _timeWindow - (DateTime.UtcNow - oldestRequest) + TimeSpan.FromSeconds(5);
        }

        if (timeToWait > TimeSpan.Zero)
        {
            // 2. Trigger the event before waiting
            try
            {
                OnRateLimitHit?.Invoke(timeToWait);
            }
            catch (Exception ex)
            {
                // Don't let event handler exceptions break the rate limiter
                _ = BugReportService.LogErrorAsync(ex, $"{context}Exception in OnRateLimitHit event handler.");
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Rate limit reached. Waiting {timeToWait.TotalSeconds:F0}s...");
            Console.ResetColor();

            await Task.Delay(timeToWait);
        }

        lock (_lock)
        {
            _requestTimestamps.Enqueue(DateTime.UtcNow);
        }
    }
}