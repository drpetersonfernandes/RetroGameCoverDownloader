using RetroGameCoverDownloader.Models;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Models;

public class CoverDownloadItemTests
{
    [Fact]
    public void CoverDownloadItem_DefaultValues_AreEmpty()
    {
        var item = new CoverDownloadItem();

        Assert.Equal(string.Empty, item.GameName);
        Assert.Equal(string.Empty, item.DownloadUrl);
        Assert.Equal(string.Empty, item.TargetFilename);
    }

    [Fact]
    public void CoverDownloadItem_CanSetProperties()
    {
        var item = new CoverDownloadItem
        {
            GameName = "Super Mario World",
            DownloadUrl = "https://example.com/image.png",
            TargetFilename = "Super Mario World.png"
        };

        Assert.Equal("Super Mario World", item.GameName);
        Assert.Equal("https://example.com/image.png", item.DownloadUrl);
        Assert.Equal("Super Mario World.png", item.TargetFilename);
    }
}
