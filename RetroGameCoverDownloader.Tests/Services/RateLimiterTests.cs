using RetroGameCoverDownloader.Services;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Services;

public class RateLimiterTests
{
    [Fact]
    public void RateLimiterAuthenticatedSetsHigherLimit()
    {
        var limiter = new RateLimiter(true);

        // Authenticated users get 4900 requests per hour, so 4900 slots should be available immediately
        var tasks = new List<Task>();
        for (var i = 0; i < 4900; i++)
        {
            tasks.Add(limiter.WaitForSlotAsync());
        }

        // All 4900 should complete without waiting (they're just enqueued)
        Assert.All(tasks, static t => Assert.Equal(TaskStatus.RanToCompletion, t.Status));
    }

    [Fact]
    public void RateLimiterUnauthenticatedSetsLowerLimit()
    {
        var limiter = new RateLimiter(false);

        var tasks = new List<Task>();
        for (var i = 0; i < 55; i++)
        {
            tasks.Add(limiter.WaitForSlotAsync());
        }

        Assert.All(tasks, static t => Assert.Equal(TaskStatus.RanToCompletion, t.Status));
    }

    [Fact]
    public async Task RateLimiterUpdateLimitChangesBehavior()
    {
        var limiter = new RateLimiter(false);

        // Use up the 55 unauthenticated slots
        for (var i = 0; i < 55; i++)
        {
            await limiter.WaitForSlotAsync();
        }

        // Update to authenticated
        limiter.UpdateLimit(true);

        // Now we should be able to use many more slots
        var tasks = new List<Task>();
        for (var i = 0; i < 100; i++)
        {
            tasks.Add(limiter.WaitForSlotAsync());
        }

        Assert.All(tasks, static t => Assert.Equal(TaskStatus.RanToCompletion, t.Status));
    }

    [Fact]
    public async Task RateLimiterOnRateLimitHitFiresEvent()
    {
        var limiter = new RateLimiter(false);
        TimeSpan? receivedWaitTime = null;
        limiter.OnRateLimitHit += waitTime => { receivedWaitTime = waitTime; };

        // Exhaust the 55 slots
        for (var i = 0; i < 55; i++)
        {
            await limiter.WaitForSlotAsync();
        }

        // The 56th request should trigger the rate limit event
        _ = limiter.WaitForSlotAsync();
        // Give it a moment to process
        await Task.Delay(100);

        Assert.NotNull(receivedWaitTime);
        Assert.True(receivedWaitTime.Value > TimeSpan.Zero);
    }

    [Fact]
    public async Task RateLimiterCancellationTokenCancelsWait()
    {
        var limiter = new RateLimiter(false);
        using var cts = new CancellationTokenSource();

        // Exhaust the 55 slots
        for (var i = 0; i < 55; i++)
        {
            await limiter.WaitForSlotAsync(cts.Token);
        }

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limiter.WaitForSlotAsync(cts.Token));
    }
}
