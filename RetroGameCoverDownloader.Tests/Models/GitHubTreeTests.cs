using RetroGameCoverDownloader.Models;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Models;

public class GitHubTreeTests
{
    [Fact]
    public void GitHubTreeDefaultValuesAreInitialized()
    {
        var tree = new GitHubTree();

        Assert.Equal(string.Empty, tree.Sha);
        Assert.Equal(string.Empty, tree.Url);
        Assert.NotNull(tree.Tree);
        Assert.Empty(tree.Tree);
        Assert.False(tree.Truncated);
    }

    [Fact]
    public void GitHubTreeItemDefaultValuesAreEmpty()
    {
        var item = new GitHubTreeItem();

        Assert.Equal(string.Empty, item.Path);
        Assert.Equal(string.Empty, item.Type);
        Assert.Equal(string.Empty, item.Mode);
        Assert.Equal(string.Empty, item.Sha);
    }

    [Fact]
    public void GitHubTreeCanAddItems()
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
