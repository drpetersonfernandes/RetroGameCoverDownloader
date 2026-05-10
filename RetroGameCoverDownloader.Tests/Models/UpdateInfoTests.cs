using RetroGameCoverDownloader.Models;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Models;

public class UpdateInfoTests
{
    [Fact]
    public void UpdateInfoCanCreateWithRequiredProperties()
    {
        var info = new UpdateInfo
        {
            LatestVersion = new Version(2, 1, 0),
            ReleaseUrl = "https://github.com/test/releases/tag/v2.1.0"
        };

        Assert.Equal(new Version(2, 1, 0), info.LatestVersion);
        Assert.Equal("https://github.com/test/releases/tag/v2.1.0", info.ReleaseUrl);
    }

    [Fact]
    public void UpdateInfoPropertiesAreCorrectlyStored()
    {
        var info = new UpdateInfo
        {
            LatestVersion = new Version(1, 0, 0),
            ReleaseUrl = "https://example.com/release"
        };

        Assert.Equal(new Version(1, 0, 0), info.LatestVersion);
        Assert.Equal("https://example.com/release", info.ReleaseUrl);
    }

    [Fact]
    public void UpdateInfoWithPreReleaseVersion()
    {
        var info = new UpdateInfo
        {
            LatestVersion = new Version(3, 0, 0, 1),
            ReleaseUrl = "https://github.com/test/releases/tag/v3.0.0.1"
        };

        Assert.Equal(new Version(3, 0, 0, 1), info.LatestVersion);
        Assert.Equal(3, info.LatestVersion.Major);
        Assert.Equal(0, info.LatestVersion.Minor);
        Assert.Equal(0, info.LatestVersion.Build);
        Assert.Equal(1, info.LatestVersion.Revision);
    }
}
