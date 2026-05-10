using RetroGameCoverDownloader.Helpers;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Helpers;

public class AppInfoTests
{
    [Fact]
    public void VersionReturnsNonNullVersion()
    {
        var version = AppInfo.Version;

        Assert.NotNull(version);
    }

    [Fact]
    public void VersionReturnsValidVersion()
    {
        var version = AppInfo.Version;

        Assert.False(string.IsNullOrEmpty(version.ToString()));
        Assert.True(version.Major >= 0);
    }

    [Fact]
    public void VersionStringReturnsNonNullAndNotEmpty()
    {
        var versionString = AppInfo.VersionString;

        Assert.NotNull(versionString);
        Assert.NotEmpty(versionString);
    }

    [Fact]
    public void VersionStringEqualsVersionToString()
    {
        var versionString = AppInfo.VersionString;
        var version = AppInfo.Version;

        Assert.Equal(version.ToString(), versionString);
    }
}
