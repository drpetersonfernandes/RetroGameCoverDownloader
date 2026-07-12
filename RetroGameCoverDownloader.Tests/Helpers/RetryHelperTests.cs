using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using RetroGameCoverDownloader.Helpers;
using RetroGameCoverDownloader.Models;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Helpers;

public class RetryHelperTests
{
    // Keep backoff tiny so retry tests run fast: delay = 2^attempt * BackoffMultiplierSeconds
    private static readonly RetrySettings FastSettings = new()
    {
        MaxRetries = 3,
        BackoffMultiplierSeconds = 0.001
    };

    #region RetryOnTransientErrorAsync

    [Fact]
    public async Task RetryOnTransientErrorAsyncSucceedsOnFirstAttemptDoesNotRetryAsync()
    {
        var calls = 0;

        var result = await RetryHelper.RetryOnTransientErrorAsync(() =>
        {
            calls++;
            return Task.FromResult("ok");
        }, FastSettings);

        Assert.Equal("ok", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RetryOnTransientErrorAsyncRetriesThenSucceedsAsync()
    {
        var calls = 0;

        var result = await RetryHelper.RetryOnTransientErrorAsync(() =>
        {
            calls++;
            if (calls < 3)
                throw new HttpRequestException("transient", null, HttpStatusCode.InternalServerError);

            return Task.FromResult("recovered");
        }, FastSettings);

        Assert.Equal("recovered", result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task RetryOnTransientErrorAsyncExhaustsRetriesThenThrowsAsync()
    {
        var calls = 0;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            RetryHelper.RetryOnTransientErrorAsync<string>(() =>
            {
                calls++;
                throw new HttpRequestException("always fails", null, HttpStatusCode.ServiceUnavailable);
            }, FastSettings));

        // Attempts 1..MaxRetries run; the last one rethrows because the guard (attempt < MaxRetries) is false
        Assert.Equal(FastSettings.MaxRetries, calls);
    }

    [Fact]
    public async Task RetryOnTransientErrorAsyncNonTransientErrorThrowsImmediatelyAsync()
    {
        var calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RetryHelper.RetryOnTransientErrorAsync<string>(() =>
            {
                calls++;
                throw new InvalidOperationException("not transient");
            }, FastSettings));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RetryOnTransientErrorAsyncWithMaxRetriesOneDoesNotRetryAsync()
    {
        var calls = 0;
        var settings = new RetrySettings { MaxRetries = 1, BackoffMultiplierSeconds = 0.001 };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            RetryHelper.RetryOnTransientErrorAsync<string>(() =>
            {
                calls++;
                throw new HttpRequestException("transient", null, HttpStatusCode.InternalServerError);
            }, settings));

        // With MaxRetries = 1 the guard (attempt < MaxRetries) is never true, so the error is thrown on the first attempt
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RetryOnTransientErrorAsyncUsesDefaultSettingsWhenNullAsync()
    {
        var result = await RetryHelper.RetryOnTransientErrorAsync(() => Task.FromResult(123));

        Assert.Equal(123, result);
    }

    [Fact]
    public async Task RetryOnTransientErrorAsyncCancellationDuringDelayThrowsAsync()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var settings = new RetrySettings { MaxRetries = 3, BackoffMultiplierSeconds = 5 };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RetryHelper.RetryOnTransientErrorAsync<string>(() =>
            {
                calls++;
                // ReSharper disable once AccessToDisposedClosure
                cts.Cancel();
                throw new HttpRequestException("transient", null, HttpStatusCode.InternalServerError);
            }, settings, cts.Token));

        Assert.Equal(1, calls);
    }

    #endregion

    #region IsTransientError

    [Fact]
    public void IsTransientErrorServerErrorReturnsTrue()
    {
        var ex = new HttpRequestException("boom", null, HttpStatusCode.InternalServerError);
        Assert.True(RetryHelper.IsTransientError(ex));
    }

    [Fact]
    public void IsTransientErrorForbiddenReturnsFalseByDefault()
    {
        var ex = new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden);
        Assert.False(RetryHelper.IsTransientError(ex));
    }

    [Fact]
    public void IsTransientErrorForbiddenReturnsTrueWhenRetryOnForbiddenEnabled()
    {
        var ex = new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden);
        var settings = new RetrySettings { RetryOnForbidden = true };
        Assert.True(RetryHelper.IsTransientError(ex, settings));
    }

    [Fact]
    public void IsTransientErrorTooManyRequestsReturnsTrue()
    {
        var ex = new HttpRequestException("rate limited", null, (HttpStatusCode)429);
        Assert.True(RetryHelper.IsTransientError(ex));
    }

    [Fact]
    public void IsTransientErrorRequestTimeoutReturnsTrue()
    {
        var ex = new HttpRequestException("timeout", null, HttpStatusCode.RequestTimeout);
        Assert.True(RetryHelper.IsTransientError(ex));
    }

    [Fact]
    public void IsTransientErrorNotFoundReturnsFalse()
    {
        var ex = new HttpRequestException("not found", null, HttpStatusCode.NotFound);
        Assert.False(RetryHelper.IsTransientError(ex));
    }

    [Fact]
    public void IsTransientErrorSocketExceptionInnerReturnsTrue()
    {
        var ex = new HttpRequestException("network", new SocketException());
        Assert.True(RetryHelper.IsTransientError(ex));
    }

    [Fact]
    public void IsTransientErrorTaskCanceledWithTimeoutReturnsTrue()
    {
        var ex = new TaskCanceledException("timed out", new TimeoutException());
        Assert.True(RetryHelper.IsTransientError(ex));
    }

    [Fact]
    public void IsTransientErrorGenericExceptionReturnsFalse()
    {
        var ex = new InvalidOperationException("nope");
        Assert.False(RetryHelper.IsTransientError(ex));
    }

    #endregion
}
