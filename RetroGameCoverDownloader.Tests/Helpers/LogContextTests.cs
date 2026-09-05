using RetroGameCoverDownloader.Helpers;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Helpers;

public class LogContextTests
{
    [Fact]
    public void ForMethodReturnsBracketedFormat()
    {
        var result = LogContext.ForMethod();

        Assert.StartsWith("[", result, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("] ", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForMethodReturnsNonNullAndNotEmpty()
    {
        var result = LogContext.ForMethod();

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void ForMethodContainsClassNameAndMethodName()
    {
        var result = LogContext.ForMethod();

        Assert.Contains("LogContextTests", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ForMethodContainsClassNameAndMethodName", result, StringComparison.OrdinalIgnoreCase);
    }
}
