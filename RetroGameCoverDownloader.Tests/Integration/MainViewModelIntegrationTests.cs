using System.IO;
using RetroGameCoverDownloader.Models;
using RetroGameCoverDownloader.Services;
using RetroGameCoverDownloader.ViewModels;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Integration;

/// <summary>
/// End-to-end integration tests for MainViewModel using the real GitHub API.
/// For each system, these tests verify the full Prepare -&gt; Download flow
/// with an actual cover image download.
///
/// Prerequisites:
/// - Internet connection
/// - GITHUB_TOKEN environment variable (strongly recommended to avoid rate limits)
/// </summary>
[Collection("GitHub Integration")]
[Trait("Category", "Integration")]
public class MainViewModelIntegrationTests
{
    [Fact]
    public void SystemsWereFetched()
    {
        if (GitHubIntegrationFixture.FetchError != null)
        {
            Assert.Fail($"Failed to fetch systems: {GitHubIntegrationFixture.FetchError}");
        }

        Assert.NotEmpty(GitHubIntegrationFixture.Systems);
    }

    [Theory]
    [MemberData(nameof(GitHubIntegrationFixture.GetSystems), MemberType = typeof(GitHubIntegrationFixture))]
    public async Task FullFlowPrepareAndDownloadOneCover(SystemConfig system, bool isSkipped)
    {
        if (isSkipped)
        {
            Assert.Fail($"Tests skipped because systems list could not be fetched: {GitHubIntegrationFixture.FetchError}");
            return;
        }

        var (_, files) = await GitHubIntegrationFixture.SharedService.GetSystemFilesAsync(system);

        if (files.Count == 0)
        {
            return; // Skip systems with no thumbnails
        }

        var firstFile = files[0];
        var gameName = Path.GetFileNameWithoutExtension(firstFile.Path);

        var vm = new IntegrationTestableMainViewModel(new AppSettings(), GitHubIntegrationFixture.SharedService);
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.FilesByPath[vm.RomFolderPath] = [$@"C:\Roms\{gameName}.rom"];
        vm.FilesByPath[vm.CoverFolderPath] = [];
        vm.SelectedSystem = system;

        await vm.PrepareDownloadAsync();

        Assert.True(vm.ItemsToDownload.Count > 0,
            $"PrepareDownloadAsync did not match any covers for {system.SystemName}");

        await vm.DownloadCoversAsync();

        Assert.True(vm.WrittenFiles.Count > 0,
            $"DownloadCoversAsync did not write any files for {system.SystemName}");

        var writtenFile = vm.WrittenFiles.Values.First();
        Assert.True(writtenFile.Length > 100,
            $"Downloaded file for {system.SystemName} is too small ({writtenFile.Length} bytes)");
    }

    private class IntegrationTestableMainViewModel : MainViewModel
    {
        public Dictionary<string, string[]> FilesByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, byte[]> WrittenFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IntegrationTestableMainViewModel(AppSettings settings, IGitHubService service)
            : base(settings, service, true)
        {
        }

        protected override void InvokeOnDispatcher(Action action)
        {
            action();
        }

        protected override void InvalidateCommands()
        {
        }

        protected override bool DirectoryExists(string path)
        {
            return true;
        }

        protected override string[] GetFiles(string path)
        {
            return FilesByPath.TryGetValue(path, out var files) ? files : [];
        }

        protected override Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken)
        {
            WrittenFiles[path] = data;
            return Task.CompletedTask;
        }

        protected override bool FileExists(string path)
        {
            return WrittenFiles.ContainsKey(path);
        }

        protected override long GetAvailableFreeSpace(string path)
        {
            return long.MaxValue;
        }

        protected override void SelectFolder(Action<string> setPath)
        {
        }
    }
}
