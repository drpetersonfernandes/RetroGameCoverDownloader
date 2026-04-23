using System.Reflection;
using RetroGameCoverDownloader.Services;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Services;

public class GitHubServiceTests
{
    [Fact]
    public void ParseGitmodules_ValidInput_ReturnsCorrectMap()
    {
        var input = "[submodule \"Nintendo - NES\"]\n" +
                    "\tpath = Nintendo - NES\n" +
                    "\turl = https://github.com/libretro-thumbnails/Nintendo_-_Nintendo_Entertainment_System.git\n" +
                    "[submodule \"Nintendo - SNES\"]\n" +
                    "\tpath = Nintendo - SNES\n" +
                    "\turl = https://github.com/libretro-thumbnails/Nintendo_-_Super_Nintendo_Entertainment_System.git\n";

        var method = typeof(GitHubService).GetMethod("ParseGitmodules", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (Dictionary<string, string>)method.Invoke(null, [input])!;

        Assert.Equal(2, result.Count);
        Assert.Equal("Nintendo_-_Nintendo_Entertainment_System", result["Nintendo - NES"]);
        Assert.Equal("Nintendo_-_Super_Nintendo_Entertainment_System", result["Nintendo - SNES"]);
    }

    [Fact]
    public void ParseGitmodules_EmptyInput_ThrowsException()
    {
        var input = "";

        var method = typeof(GitHubService).GetMethod("ParseGitmodules", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [input]));
    }

    [Fact]
    public void ParseGitmodules_MalformedLines_SkipsInvalidEntries()
    {
        var input = "[submodule \"Bad\"]\n" +
                    "\tpath = Bad\n" +
                    "\turl = /\n" +
                    "[submodule \"Good\"]\n" +
                    "\tpath = Good\n" +
                    "\turl = https://github.com/libretro-thumbnails/Good_System.git\n";

        var method = typeof(GitHubService).GetMethod("ParseGitmodules", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (Dictionary<string, string>)method.Invoke(null, [input])!;

        Assert.Single(result);
        Assert.Equal("Good_System", result["Good"]);
    }
}
