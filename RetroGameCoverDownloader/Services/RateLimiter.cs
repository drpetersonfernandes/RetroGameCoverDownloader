using Serilog;

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

    public async Task WaitForSlotAsync(CancellationToken cancellationToken = default)
    {
        const string context = "[RateLimiter.WaitForSlotAsync] ";

        TimeSpan timeToWait;
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            while (_requestTimestamps.Count > 0 && (now - _requestTimestamps.Peek()) > _timeWindow)
            {
                _requestTimestamps.Dequeue();
            }

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
            try
            {
                OnRateLimitHit?.Invoke(timeToWait);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "{Context}Exception in OnRateLimitHit event handler.", context);
            }

            await Task.Delay(timeToWait, cancellationToken);
        }

        lock (_lock)
        {
            _requestTimestamps.Enqueue(DateTime.UtcNow);
        }
    }
}