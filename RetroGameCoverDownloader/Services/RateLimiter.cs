namespace RetroGameCoverDownloader.Services;

public class RateLimiter
{
    private readonly int _maxRequests;
    private readonly TimeSpan _timeWindow;
    private readonly Queue<DateTime> _requestTimestamps = new();
    private readonly object _lock = new();

    public RateLimiter(bool isAuthenticated)
    {
        _maxRequests = isAuthenticated ? 4900 : 55; // Stay safely below the limit
        _timeWindow = TimeSpan.FromHours(1);
        Console.WriteLine($"Rate limiter configured for {_maxRequests} requests per hour.");
    }

    public async Task WaitForSlotAsync()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            // Clear out old timestamps
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
            timeToWait = _timeWindow - (DateTime.UtcNow - oldestRequest) + TimeSpan.FromSeconds(5); // Add a 5-second buffer
        }

        if (timeToWait > TimeSpan.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Rate limit reached. Waiting for {timeToWait.TotalSeconds:F0} seconds to avoid API errors...");
            Console.ResetColor();
            await Task.Delay(timeToWait);
        }

        // After waiting, add the new request timestamp
        lock (_lock)
        {
            _requestTimestamps.Enqueue(DateTime.UtcNow);
        }
    }
}