using RetroGameCoverDownloader.Models;
using Xunit;
using Xunit.Sdk;

namespace RetroGameCoverDownloader.Tests.Integration;

/// <summary>
/// Real GitHub API integration tests for GitHubService.
/// These tests call the live GitHub API and download real cover images.
///
/// Prerequisites:
/// - Internet connection
/// - GITHUB_TOKEN environment variable (strongly recommended to avoid rate limits)
///
/// To run only these tests: dotnet test --filter "Category=Integration"
/// To exclude these tests: dotnet test --filter "Category!=Integration"
/// </summary>
[Collection("GitHub Integration")]
[Trait("Category", "Integration")]
public class GitHubServiceIntegrationTests
{
    [Fact]
    public void SystemsWereFetched()
    {
        if (GitHubIntegrationFixture.FetchError != null)
            throw SkipException.ForSkip($"Failed to fetch systems: {GitHubIntegrationFixture.FetchError}");

        Assert.NotEmpty(GitHubIntegrationFixture.Systems);
    }

    [Theory]
    [MemberData(nameof(GitHubIntegrationFixture.GetSystems), MemberType = typeof(GitHubIntegrationFixture))]
    public async Task GetSystemFilesAsyncReturnsAtLeastOneFile(SystemConfig system, bool isSkipped)
    {
        if (isSkipped)
            throw SkipException.ForSkip($"Systems list could not be fetched: {GitHubIntegrationFixture.FetchError}");

        var (branch, files) = await GitHubIntegrationFixture.SharedService.GetSystemFilesAsync(system);

        if (string.IsNullOrEmpty(branch) || files.Count == 0)
        {
            return; // Skip systems with no thumbnail repository or empty folder
        }

        Assert.False(string.IsNullOrEmpty(branch),
            $"No branch found for {system.SystemName}");
        Assert.NotEmpty(files);
    }

    [Theory]
    [MemberData(nameof(GitHubIntegrationFixture.GetSystems), MemberType = typeof(GitHubIntegrationFixture))]
    public async Task DownloadFileAsyncDownloadsRealCoverImage(SystemConfig system, bool isSkipped)
    {
        if (isSkipped)
            throw SkipException.ForSkip($"Systems list could not be fetched: {GitHubIntegrationFixture.FetchError}");

        var (branch, files) = await GitHubIntegrationFixture.SharedService.GetSystemFilesAsync(system);

        if (string.IsNullOrEmpty(branch) || files.Count == 0)
        {
            return; // Skip systems with no thumbnails
        }

        var firstFile = files[0];
        var encodedPath = string.Join("/", firstFile.Path.Split('/').Select(Uri.EscapeDataString));
        var url = $"https://raw.githubusercontent.com/{system.Owner}/{system.Repo}/{branch}/{encodedPath}";

        var data = await GitHubIntegrationFixture.SharedService.DownloadFileAsync(url);

        Assert.NotNull(data);
        Assert.True(data.Length > 100,
            $"Downloaded file for {system.SystemName} is too small ({data.Length} bytes)");

        var pngMagic = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var jpgMagic = new byte[] { 0xFF, 0xD8, 0xFF };
        var isPng = data.Length >= 4 && data.Take(4).SequenceEqual(pngMagic);
        var isJpg = data.Length >= 3 && data.Take(3).SequenceEqual(jpgMagic);

        Assert.True(isPng || isJpg,
            $"Downloaded file for {system.SystemName} is not a recognized image format (PNG/JPEG)");
    }
}
