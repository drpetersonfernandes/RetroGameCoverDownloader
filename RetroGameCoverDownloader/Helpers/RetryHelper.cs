using System.Net;
using System.Net.Http;
using RetroGameCoverDownloader.Models;

namespace RetroGameCoverDownloader.Helpers;

public static class RetryHelper
{
    public static async Task<T> RetryOnTransientErrorAsync<T>(
        Func<Task<T>> action,
        RetrySettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        settings ??= RetrySettings.Default;

        for (var attempt = 1; attempt <= settings.MaxRetries; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < settings.MaxRetries && IsTransientError(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * settings.BackoffMultiplierSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        return await action();
    }

    internal static bool IsTransientError(Exception ex)
    {
        if (ex is HttpRequestException httpEx)
        {
            return httpEx.StatusCode switch
            {
                null => true,
                >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError => httpEx.StatusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429,
                _ => httpEx.InnerException is System.Net.Sockets.SocketException
            };
        }

        return ex is TaskCanceledException { InnerException: TimeoutException };
    }
}
