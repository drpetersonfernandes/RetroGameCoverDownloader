using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using RetroGameCoverDownloader.Managers;
using RetroGameCoverDownloader.Models;
using RetroGameCoverDownloader.Services;
using RetroGameCoverDownloader.ViewModels;
using Xunit;

namespace RetroGameCoverDownloader.Tests.ViewModels;

public class MainViewModelTests
{
    #region Helpers

    private static MainViewModel CreateViewModel(AppSettings? settings = null, IGitHubService? service = null)
        => new TestableMainViewModel(settings ?? new AppSettings(), service ?? new FakeGitHubService());

    private static void SetCts(MainViewModel vm, CancellationTokenSource cts)
    {
        var field = typeof(MainViewModel).GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(vm, cts);
    }

    private static string CreateTempSettingsFile(AppSettings? settings = null)
    {
        var path = Path.GetTempFileName();
        var serializer = new XmlSerializer(typeof(AppSettings));
        using var writer = new StreamWriter(path);
        serializer.Serialize(writer, settings ?? new AppSettings());
        return path;
    }

    private class FakeGitHubService : IGitHubService
    {
        public event Action<TimeSpan>? RateLimitHit;
        public Func<Action<string>, CancellationToken, Task<List<SystemConfig>>>? OnGetAvailableSystemsAsync { get; set; }
        public Func<SystemConfig, Action<string>, CancellationToken, Task<(string, List<GitHubTreeItem>)>>? OnGetSystemFilesAsync { get; set; }
        public Func<string, Action<string>?, CancellationToken, Task<byte[]?>>? OnDownloadFileAsync { get; set; }
        public bool Disposed { get; private set; }

        public Task<List<SystemConfig>> GetAvailableSystemsAsync(Action<string> logAction, CancellationToken cancellationToken = default)
            => OnGetAvailableSystemsAsync?.Invoke(logAction, cancellationToken) ?? Task.FromResult(new List<SystemConfig>());

        public Task<(string Branch, List<GitHubTreeItem> Files)> GetSystemFilesAsync(SystemConfig system, Action<string> logAction, CancellationToken cancellationToken = default)
            => OnGetSystemFilesAsync?.Invoke(system, logAction, cancellationToken) ?? Task.FromResult((string.Empty, new List<GitHubTreeItem>()));

        public Task<byte[]?> DownloadFileAsync(string url, Action<string>? logAction = null, CancellationToken cancellationToken = default)
            => OnDownloadFileAsync?.Invoke(url, logAction, cancellationToken) ?? Task.FromResult<byte[]?>(null);

        public void Dispose() => Disposed = true;
    }

    private class TestableMainViewModel : MainViewModel
    {
        public TestableMainViewModel(AppSettings settings, IGitHubService service) : base(settings, service, suppressStartup: true) { }

        public Dictionary<string, string[]> FilesByPath { get; } = new();
        public Dictionary<string, byte[]> WrittenFiles { get; } = new();
        public long AvailableFreeSpace { get; set; } = long.MaxValue;
        public Func<string, bool>? DirectoryExistsOverride { get; set; }

        protected override void InvokeOnDispatcher(Action action) => action();
        protected override void InvalidateCommands() { }
        protected override bool DirectoryExists(string path) => DirectoryExistsOverride?.Invoke(path) ?? true;
        protected override string[] GetFiles(string path) => FilesByPath.TryGetValue(path, out var files) ? files : Array.Empty<string>();
        protected override Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken)
        {
            WrittenFiles[path] = data;
            return Task.CompletedTask;
        }
        protected override bool FileExists(string path) => WrittenFiles.ContainsKey(path);
        protected override long GetAvailableFreeSpace(string path) => AvailableFreeSpace;
        protected override void SelectFolder(Action<string> setPath) { }
    }

    #endregion

    #region CanExecute

    [Fact]
    public void PrepareCommand_CanExecute_WhenNotBusyAndFoldersSet_ReturnsTrue()
    {
        var vm = CreateViewModel();
        vm.IsBusy = false;
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        Assert.True(vm.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public void PrepareCommand_CanExecute_WhenBusy_ReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = true;
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        Assert.False(vm.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public void PrepareCommand_CanExecute_WhenMissingFolder_ReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = false;
        vm.RomFolderPath = "";
        vm.CoverFolderPath = @"C:\Covers";
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        Assert.False(vm.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public void PrepareCommand_CanExecute_WhenNoSystem_ReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = false;
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.SelectedSystem = null;

        Assert.False(vm.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public void DownloadCommand_CanExecute_WhenItemsExistAndNotBusy_ReturnsTrue()
    {
        var vm = CreateViewModel();
        vm.IsBusy = false;
        vm._itemsToDownload.Add(new CoverDownloadItem { GameName = "Test" });

        Assert.True(vm.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public void DownloadCommand_CanExecute_WhenNoItems_ReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = false;

        Assert.False(vm.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public void DownloadCommand_CanExecute_WhenBusy_ReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = true;
        vm._itemsToDownload.Add(new CoverDownloadItem { GameName = "Test" });

        Assert.False(vm.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public void CancelCommand_CanExecute_WhenBusyAndCtsSet_ReturnsTrue()
    {
        var vm = CreateViewModel();
        vm.IsBusy = true;
        SetCts(vm, new CancellationTokenSource());

        Assert.True(vm.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void CancelCommand_CanExecute_WhenNotBusy_ReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = false;

        Assert.False(vm.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void CancelCommand_CanExecute_WhenBusyButNoCts_ReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = true;

        Assert.False(vm.CancelCommand.CanExecute(null));
    }

    #endregion

    #region PrepareDownloadAsync

    [Fact]
    public async Task PrepareDownloadAsync_MissingDirectory_LogsErrorAndSetsBusyFalse()
    {
        var vm = (TestableMainViewModel)CreateViewModel();
        vm.DirectoryExistsOverride = _ => false;
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        await vm.PrepareDownloadAsync();

        Assert.False(vm.IsBusy);
        Assert.Empty(vm._itemsToDownload);
    }

    [Fact]
    public async Task PrepareDownloadAsync_NoMissingCovers_LeavesItemsEmpty()
    {
        var fake = new FakeGitHubService();
        var vm = (TestableMainViewModel)CreateViewModel(service: fake);
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.FilesByPath[vm.RomFolderPath] = new[] { @"C:\Roms\Game1.nes" };
        vm.FilesByPath[vm.CoverFolderPath] = new[] { @"C:\Covers\Game1.png" };
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        fake.OnGetSystemFilesAsync = (_, _, _) => Task.FromResult(("main", new List<GitHubTreeItem>
        {
            new GitHubTreeItem { Path = "Named_Boxarts/Game1.png", Type = "blob" }
        }));

        await vm.PrepareDownloadAsync();

        Assert.Empty(vm._itemsToDownload);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task PrepareDownloadAsync_MatchingCovers_PopulatesItemsToDownload()
    {
        var fake = new FakeGitHubService();
        var vm = (TestableMainViewModel)CreateViewModel(service: fake);
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.FilesByPath[vm.RomFolderPath] = new[] { @"C:\Roms\Super Mario Bros.nes" };
        vm.FilesByPath[vm.CoverFolderPath] = Array.Empty<string>();
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        fake.OnGetSystemFilesAsync = (_, _, _) => Task.FromResult(("main", new List<GitHubTreeItem>
        {
            new GitHubTreeItem { Path = "Named_Boxarts/Super Mario Bros.png", Type = "blob" }
        }));

        await vm.PrepareDownloadAsync();

        Assert.Single(vm._itemsToDownload);
        Assert.Equal("Super Mario Bros", vm._itemsToDownload[0].GameName);
        Assert.Equal("Super Mario Bros.png", vm._itemsToDownload[0].TargetFilename);
        Assert.Contains("raw.githubusercontent.com", vm._itemsToDownload[0].DownloadUrl);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task PrepareDownloadAsync_GitHubFilesEmpty_LeavesItemsEmpty()
    {
        var fake = new FakeGitHubService();
        var vm = (TestableMainViewModel)CreateViewModel(service: fake);
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.FilesByPath[vm.RomFolderPath] = new[] { @"C:\Roms\Game1.nes" };
        vm.FilesByPath[vm.CoverFolderPath] = Array.Empty<string>();
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        fake.OnGetSystemFilesAsync = (_, _, _) => Task.FromResult(("main", new List<GitHubTreeItem>()));

        await vm.PrepareDownloadAsync();

        Assert.Empty(vm._itemsToDownload);
        Assert.False(vm.IsBusy);
    }

    #endregion

    #region DownloadCoversAsync

    [Fact]
    public async Task DownloadCoversAsync_NoItems_ReturnsImmediately()
    {
        var vm = CreateViewModel();

        await vm.DownloadCoversAsync();

        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task DownloadCoversAsync_SuccessfulDownload_WritesFilesAndUpdatesProgress()
    {
        var fake = new FakeGitHubService();
        var vm = (TestableMainViewModel)CreateViewModel(service: fake);
        vm.CoverFolderPath = @"C:\Covers";
        vm._itemsToDownload.Add(new CoverDownloadItem
        {
            GameName = "Super Mario Bros",
            TargetFilename = "Super Mario Bros.png",
            DownloadUrl = "http://example.com/cover.png"
        });

        fake.OnDownloadFileAsync = (_, _, _) => Task.FromResult<byte[]?>(new byte[] { 1, 2, 3 });

        await vm.DownloadCoversAsync();

        Assert.Single(vm.WrittenFiles);
        Assert.Equal(new byte[] { 1, 2, 3 }, vm.WrittenFiles[@"C:\Covers\Super Mario Bros.png"]);
        Assert.Equal(1, vm.ProgressValue);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task DownloadCoversAsync_LowDiskSpace_AbortsWithIOException()
    {
        var fake = new FakeGitHubService();
        var vm = (TestableMainViewModel)CreateViewModel(service: fake);
        vm.CoverFolderPath = @"C:\Covers";
        vm.AvailableFreeSpace = 0;
        vm._itemsToDownload.Add(new CoverDownloadItem
        {
            GameName = "Super Mario Bros",
            TargetFilename = "Super Mario Bros.png",
            DownloadUrl = "http://example.com/cover.png"
        });

        fake.OnDownloadFileAsync = (_, _, _) => Task.FromResult<byte[]?>(new byte[] { 1, 2, 3 });

        await vm.DownloadCoversAsync();

        Assert.False(vm.IsBusy);
        Assert.Empty(vm.WrittenFiles);
    }

    [Fact]
    public async Task DownloadCoversAsync_NullData_SkipsItem()
    {
        var fake = new FakeGitHubService();
        var vm = (TestableMainViewModel)CreateViewModel(service: fake);
        vm.CoverFolderPath = @"C:\Covers";
        vm._itemsToDownload.Add(new CoverDownloadItem
        {
            GameName = "Super Mario Bros",
            TargetFilename = "Super Mario Bros.png",
            DownloadUrl = "http://example.com/cover.png"
        });

        fake.OnDownloadFileAsync = (_, _, _) => Task.FromResult<byte[]?>(null);

        await vm.DownloadCoversAsync();

        Assert.Empty(vm.WrittenFiles);
        Assert.Equal(1, vm.ProgressValue);
        Assert.False(vm.IsBusy);
    }

    #endregion

    #region Disposal

    [Fact]
    public void Dispose_CallsGitHubServiceDispose()
    {
        var fake = new FakeGitHubService();
        var vm = CreateViewModel(service: fake);

        vm.Dispose();

        Assert.True(fake.Disposed);
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        var vm = CreateViewModel();

        var ex = Record.Exception(() =>
        {
            vm.Dispose();
            vm.Dispose();
        });

        Assert.Null(ex);
    }

    #endregion

    #region Token / Proxy Updates

    [Fact]
    public void UpdateToken_SwapsServiceAndDisposesOld()
    {
        var oldService = new FakeGitHubService();
        var newService = new FakeGitHubService();
        var vm = new TestableMainViewModelWithFactory(oldService, newService);

        var originalPath = SettingsManager.SettingsFilePath;
        try
        {
            SettingsManager.SettingsFilePath = CreateTempSettingsFile();
            vm.UpdateToken("new-token");
        }
        finally
        {
            SettingsManager.SettingsFilePath = originalPath;
        }

        Assert.True(oldService.Disposed);
        Assert.Contains("token updated", vm.LogText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateProxySettings_SwapsServiceAndDisposesOld()
    {
        var oldService = new FakeGitHubService();
        var newService = new FakeGitHubService();
        var vm = new TestableMainViewModelWithFactory(oldService, newService);

        vm.UpdateProxySettings(true, "proxy", 8080, "user", "pass");

        Assert.True(oldService.Disposed);
        Assert.Contains("Proxy settings updated", vm.LogText, StringComparison.OrdinalIgnoreCase);
    }

    private class TestableMainViewModelWithFactory : MainViewModel
    {
        private readonly FakeGitHubService _next;

        public TestableMainViewModelWithFactory(FakeGitHubService initial, FakeGitHubService next)
            : base(new AppSettings(), initial, suppressStartup: true)
        {
            _next = next;
        }

        protected override IGitHubService CreateGitHubService(string? token, bool useProxy, string? proxyHost, int proxyPort, string? proxyUsername, string? proxyPassword)
            => _next;
    }

    #endregion
}
