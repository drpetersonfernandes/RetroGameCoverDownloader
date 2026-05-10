namespace RetroGameCoverDownloader.Models;

public class RetrySettings
{
    public int MaxRetries { get; init; } = 3;
    public double BackoffMultiplierSeconds { get; init; } = 1.5;
    public int CircuitBreakerThreshold { get; init; } = 5;
    public int CircuitBreakerCooldownSeconds { get; init; } = 30;

    public static RetrySettings Default { get; } = new();
}
