using RetroGameCoverDownloader.Models;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Models;

public class RetrySettingsTests
{
    [Fact]
    public void RetrySettingsDefaultValuesAreExpected()
    {
        var settings = new RetrySettings();

        Assert.Equal(3, settings.MaxRetries);
        Assert.Equal(1.5, settings.BackoffMultiplierSeconds);
        Assert.Equal(5, settings.CircuitBreakerThreshold);
        Assert.Equal(30, settings.CircuitBreakerCooldownSeconds);
    }

    [Fact]
    public void DefaultReturnsNonNullInstanceWithDefaults()
    {
        var settings = RetrySettings.Default;

        Assert.NotNull(settings);
        Assert.Equal(3, settings.MaxRetries);
        Assert.Equal(1.5, settings.BackoffMultiplierSeconds);
        Assert.Equal(5, settings.CircuitBreakerThreshold);
        Assert.Equal(30, settings.CircuitBreakerCooldownSeconds);
    }

    [Fact]
    public void DefaultReturnsSameInstanceEachTime()
    {
        var first = RetrySettings.Default;
        var second = RetrySettings.Default;

        Assert.Same(first, second);
    }

    [Fact]
    public void RetrySettingsCanCustomizeValuesViaInit()
    {
        var settings = new RetrySettings
        {
            MaxRetries = 10,
            BackoffMultiplierSeconds = 0.25,
            CircuitBreakerThreshold = 2,
            CircuitBreakerCooldownSeconds = 60
        };

        Assert.Equal(10, settings.MaxRetries);
        Assert.Equal(0.25, settings.BackoffMultiplierSeconds);
        Assert.Equal(2, settings.CircuitBreakerThreshold);
        Assert.Equal(60, settings.CircuitBreakerCooldownSeconds);
    }
}
