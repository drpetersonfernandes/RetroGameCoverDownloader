using RetroGameCoverDownloader.Models;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Models;

public class GitHubTreeTests
{
    [Fact]
    public void GitHubTree_DefaultValues_AreInitialized()
    {
        var tree = new GitHubTree();

        Assert.Equal(string.Empty, tree.Sha);
        Assert.Equal(string.Empty, tree.Url);
        Assert.NotNull(tree.Tree);
        Assert.Empty(tree.Tree);
        Assert.False(tree.Truncated);
    }

    [Fact]
    public void GitHubTreeItem_DefaultValues_AreEmpty()
    {
        var item = new GitHubTreeItem();

        Assert.Equal(string.Empty, item.Path);
        Assert.Equal(string.Empty, item.Type);
        Assert.Equal(string.Empty, item.Mode);
        Assert.Equal(string.Empty, item.Sha);
    }

    [Fact]
    public void GitHubTree_CanAddItems()
    {
        var tree = new GitHubTree
        {
            Tree =
            [
                new GitHubTreeItem { Path = "Named_Boxarts/game.png", Type = "blob" }
            ]
        };

        Assert.Single(tree.Tree);
        Assert.Equal("Named_Boxarts/game.png", tree.Tree[0].Path);
        Assert.Equal("blob", tree.Tree[0].Type);
    }
}
