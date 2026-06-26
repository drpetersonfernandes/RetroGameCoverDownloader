using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using RetroGameCoverDownloader.Models;

namespace RetroGameCoverDownloader.Helpers;

public static class RetryHelper
{
    public static async Task<T> RetryOnTransientErrorAsync<T>(
        Func<Task<T>> action,
        RetrySettings? settings = null,
        Action<string>? logAction = null,
        CancellationToken cancellationToken = default)
    {
        var retrySettings = settings ?? RetrySettings.Default;

        for (var attempt = 1; attempt <= retrySettings.MaxRetries; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < retrySettings.MaxRetries && IsTransientError(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * retrySettings.BackoffMultiplierSeconds);
                logAction?.Invoke($"Retry {attempt}/{retrySettings.MaxRetries} after {delay.TotalSeconds:F0}s: {ex.GetType().Name}");
                await Task.Delay(delay, cancellationToken);
            }
        }

        return await action();
    }

    private const HttpStatusCode TooManyRequests = (HttpStatusCode)429;

    public static bool IsTransientError(Exception ex)
    {
        if (ex is HttpRequestException httpEx)
        {
            var statusCode = httpEx.StatusCode;

            return statusCode switch
            {
                >= HttpStatusCode.InternalServerError => true,
                HttpStatusCode.Forbidden => true,
                >= HttpStatusCode.BadRequest => statusCode is HttpStatusCode.RequestTimeout or TooManyRequests,
                _ => httpEx.InnerException is SocketException
            };
        }

        return ex is TaskCanceledException { InnerException: TimeoutException };
    }
}
