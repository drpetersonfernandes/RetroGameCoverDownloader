using RetroGameCoverDownloader.Models;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Models;

public class SystemConfigTests
{
    [Fact]
    public void SystemConfigConstructorSetsProperties()
    {
        var config = new SystemConfig("Nintendo - SNES", "libretro-thumbnails", "Nintendo_-_Super_Nintendo_Entertainment_System", "Named_Boxarts");

        Assert.Equal("Nintendo - SNES", config.SystemName);
        Assert.Equal("libretro-thumbnails", config.Owner);
        Assert.Equal("Nintendo_-_Super_Nintendo_Entertainment_System", config.Repo);
        Assert.Equal("Named_Boxarts", config.FolderPath);
    }

    [Fact]
    public void SystemConfigPropertiesAreReadOnly()
    {
        var config = new SystemConfig("Test", "Owner", "Repo", "Folder");

        Assert.Equal("Test", config.SystemName);
        Assert.Equal("Owner", config.Owner);
        Assert.Equal("Repo", config.Repo);
        Assert.Equal("Folder", config.FolderPath);
    }
}
