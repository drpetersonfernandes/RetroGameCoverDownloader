using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using RetroGameCoverDownloader.Helpers;
using RetroGameCoverDownloader.Models;
using RetroGameCoverDownloader.Services;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Services;

public class UpdateCheckerServiceTests
{
    #region UpdateAvailable Event Tests

    [Fact]
    public void UpdateAvailableCanSubscribeAndReceiveEvent()
    {
        UpdateInfo? receivedInfo = null;
        Action<UpdateInfo> handler = info => { receivedInfo = info; };
        UpdateCheckerService.UpdateAvailable += handler;

        try
        {
            var testInfo = new UpdateInfo
            {
                LatestVersion = new Version(2, 0, 0),
                ReleaseUrl = "https://github.com/test/releases/tag/v2.0.0"
            };

            typeof(UpdateCheckerService)
                .GetField("UpdateAvailable", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null)
                ?.GetType()
                .GetMethod("Invoke")
                ?.Invoke(
                    typeof(UpdateCheckerService)
                        .GetField("UpdateAvailable", BindingFlags.Static | BindingFlags.NonPublic)
                        ?.GetValue(null),
                    [testInfo]);

            Assert.NotNull(receivedInfo);
            Assert.Equal(new Version(2, 0, 0), receivedInfo!.LatestVersion);
            Assert.Equal("https://github.com/test/releases/tag/v2.0.0", receivedInfo.ReleaseUrl);
        }
        finally
        {
            UpdateCheckerService.UpdateAvailable -= handler;
        }
    }

    [Fact]
    public void UpdateAvailableCanUnsubscribe()
    {
        var callCount = 0;
        Action<UpdateInfo> handler = _ => { callCount++; };
        UpdateCheckerService.UpdateAvailable += handler;
        UpdateCheckerService.UpdateAvailable -= handler;

        var testInfo = new UpdateInfo
        {
            LatestVersion = new Version(2, 0, 0),
            ReleaseUrl = "https://github.com/test/releases/tag/v2.0.0"
        };

        typeof(UpdateCheckerService)
            .GetField("UpdateAvailable", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null)
            ?.GetType()
            .GetMethod("Invoke")
            ?.Invoke(
                typeof(UpdateCheckerService)
                    .GetField("UpdateAvailable", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.GetValue(null),
                [testInfo]);

        Assert.Equal(0, callCount);
    }

    #endregion

    #region IsTransientError Tests

    [Fact]
    public void IsTransientErrorTaskCanceledExceptionWithTimeoutInnerReturnsTrue()
    {
        var ex = new TaskCanceledException("timeout", new TimeoutException());

        var result = RetryHelper.IsTransientError(ex);

        Assert.True(result);
    }

    [Fact]
    public void IsTransientErrorTaskCanceledExceptionWithoutTimeoutInnerReturnsFalse()
    {
        var ex = new TaskCanceledException();

        var result = RetryHelper.IsTransientError(ex);

        Assert.False(result);
    }

    [Fact]
    public void IsTransientErrorHttpRequestExceptionWithSocketExceptionInnerReturnsTrue()
    {
        var ex = new HttpRequestException(null, new SocketException());

        var result = RetryHelper.IsTransientError(ex);

        Assert.True(result);
    }

    [Fact]
    public void IsTransientErrorHttpRequestExceptionWithoutSocketExceptionInnerReturnsFalse()
    {
        var ex = new HttpRequestException("generic error");

        var result = RetryHelper.IsTransientError(ex);

        Assert.False(result);
    }

    [Fact]
    public void IsTransientErrorGenericExceptionReturnsFalse()
    {
        var ex = new InvalidOperationException("some error");

        var result = RetryHelper.IsTransientError(ex);

        Assert.False(result);
    }

    #endregion

    #region Regex Tests

    [Theory]
    [InlineData("v1.2.3", true)]
    [InlineData("1.2.3", true)]
    [InlineData("v1.2.3.4", true)]
    [InlineData("v10.20.30", true)]
    [InlineData("v0.0.1", true)]
    [InlineData("abc", false)]
    [InlineData("v1.2", false)]
    [InlineData("", false)]
    public void RegexMatchesVersionPatterns(string input, bool shouldMatch)
    {
        var regex = GetMyRegex();

        var result = regex.IsMatch(input);

        Assert.Equal(shouldMatch, result);
    }

    #endregion

    #region Helpers

    private static System.Text.RegularExpressions.Regex GetMyRegex()
    {
        var method = typeof(UpdateCheckerService).GetMethod(
            "MyRegex",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        return (System.Text.RegularExpressions.Regex)method.Invoke(null, null)!;
    }

    #endregion
}
