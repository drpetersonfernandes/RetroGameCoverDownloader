namespace RetroGameCoverDownloader.Models;

public class RetrySettings
{
    public int MaxRetries { get; init; } = 3;
    public double BackoffMultiplierSeconds { get; init; } = 1.5;
    public int CircuitBreakerThreshold { get; init; } = 5;
    public int CircuitBreakerCooldownSeconds { get; init; } = 30;

    // A GitHub 403 is almost always a (primary or secondary) rate limit. Retrying the
    // identical request with exponential backoff rarely helps and just delays the
    // cache/next-branch fallback, so 403s are not retried by default. Opt in when a
    // specific caller genuinely benefits from retrying.
    public bool RetryOnForbidden { get; init; }

    public static RetrySettings Default { get; } = new();
}
