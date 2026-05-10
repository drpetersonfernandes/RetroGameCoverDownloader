using RetroGameCoverDownloader.Helpers;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Helpers;

public class LogContextTests
{
    [Fact]
    public void ForMethodReturnsBracketedFormat()
    {
        var result = LogContext.ForMethod();

        Assert.StartsWith("[", result);
        Assert.EndsWith("] ", result);
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

        Assert.Contains("LogContextTests", result);
        Assert.Contains("ForMethodContainsClassNameAndMethodName", result);
    }
}
