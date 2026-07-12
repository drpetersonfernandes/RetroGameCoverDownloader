using System.Reflection;
using RetroGameCoverDownloader.Services;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Services;

public class UiLogSinkTests
{
    private static readonly DateTimeOffset FixedTimestamp =
        new(2026, 1, 2, 13, 45, 09, TimeSpan.Zero);

    private static LogEvent CreateLogEvent(LogEventLevel level, string message)
    {
        var template = new MessageTemplateParser().Parse(message);
        return new LogEvent(
            FixedTimestamp,
            level,
            null,
            template,
            []);
    }

    private static string InvokeFormatLogEvent(LogEvent logEvent)
    {
        var method = typeof(UiLogSink).GetMethod(
            "FormatLogEvent",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        return (string)method.Invoke(null, [logEvent])!;
    }

    #region FormatLogEvent

    [Theory]
    [InlineData(LogEventLevel.Error)]
    [InlineData(LogEventLevel.Fatal)]
    public void FormatLogEventErrorAndFatalUseErrorPrefix(LogEventLevel level)
    {
        var logEvent = CreateLogEvent(level, "something broke");

        var result = InvokeFormatLogEvent(logEvent);

        Assert.Equal("[13:45:09] ERROR: something broke", result);
    }

    [Fact]
    public void FormatLogEventWarningUsesWarningPrefix()
    {
        var logEvent = CreateLogEvent(LogEventLevel.Warning, "be careful");

        var result = InvokeFormatLogEvent(logEvent);

        Assert.Equal("[13:45:09] WARNING: be careful", result);
    }

    [Theory]
    [InlineData(LogEventLevel.Information)]
    [InlineData(LogEventLevel.Debug)]
    [InlineData(LogEventLevel.Verbose)]
    public void FormatLogEventOtherLevelsHaveNoPrefix(LogEventLevel level)
    {
        var logEvent = CreateLogEvent(level, "just info");

        var result = InvokeFormatLogEvent(logEvent);

        Assert.Equal("[13:45:09] just info", result);
    }

    #endregion
}
