using System.IO;
using System.Reflection;
using RetroGameCoverDownloader.Managers;
using RetroGameCoverDownloader.Models;
using RetroGameCoverDownloader.Services;
using RetroGameCoverDownloader.ViewModels;
using Xunit;

namespace RetroGameCoverDownloader.Tests.ViewModels;

public class MainViewModelTests
{
    #region Helpers

    private static TestableMainViewModel CreateViewModel(AppSettings? settings = null, IGitHubService? service = null)
    {
        return new TestableMainViewModel(settings ?? new AppSettings(), service ?? new FakeGitHubService());
    }

    private static void SetCts(MainViewModel vm, CancellationTokenSource cts)
    {
        var field = typeof(MainViewModel).GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null) field.SetValue(vm, cts);
    }

    private static string CreateTempSettingsFile(AppSettings? settings = null)
    {
        var path = Path.GetTempFileName();
        SettingsManager.SaveSettings(settings ?? new AppSettings(), path);
        return path;
    }

    private class FakeGitHubService : IGitHubService
    {
#pragma warning disable CS0067
        public event Action<TimeSpan>? RateLimitHit;
        public event Action? UnauthorizedAccess;
#pragma warning restore CS0067
        public Func<CancellationToken, Task<List<SystemConfig>>>? OnGetAvailableSystemsAsync { get; set; }
        public Func<SystemConfig, CancellationToken, Task<(string, List<GitHubTreeItem>)>>? OnGetSystemFilesAsync { get; set; }
        public Func<string, CancellationToken, Task<byte[]?>>? OnDownloadFileAsync { get; set; }
        public bool Disposed { get; private set; }

        public Task<List<SystemConfig>> GetAvailableSystemsAsync(CancellationToken cancellationToken = default)
        {
            return OnGetAvailableSystemsAsync?.Invoke(cancellationToken) ?? Task.FromResult(new List<SystemConfig>());
        }

        public Task<(string Branch, List<GitHubTreeItem> Files)> GetSystemFilesAsync(SystemConfig system, CancellationToken cancellationToken = default)
        {
            return OnGetSystemFilesAsync?.Invoke(system, cancellationToken) ?? Task.FromResult((string.Empty, new List<GitHubTreeItem>()));
        }

        public Task<byte[]?> DownloadFileAsync(string url, CancellationToken cancellationToken = default)
        {
            return OnDownloadFileAsync?.Invoke(url, cancellationToken) ?? Task.FromResult<byte[]?>(null);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private class TestableMainViewModel : MainViewModel
    {
        public TestableMainViewModel(AppSettings settings, IGitHubService service) : base(settings, service, true)
        {
        }

        public Dictionary<string, string[]> FilesByPath { get; } = new();
        public Dictionary<string, byte[]> WrittenFiles { get; } = new();
        public long AvailableFreeSpace { get; set; } = long.MaxValue;
        public Func<string, bool>? DirectoryExistsOverride { get; set; }

        protected override void InvokeOnDispatcher(Action action)
        {
            action();
        }

        protected override void InvalidateCommands()
        {
        }

        protected override bool DirectoryExists(string path)
        {
            return DirectoryExistsOverride?.Invoke(path) ?? true;
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
            return AvailableFreeSpace;
        }

        protected override void SelectFolder(Action<string> setPath)
        {
        }
    }

    #endregion

    #region CanExecute

    [Fact]
    public void PrepareCommandCanExecuteWhenNotBusyAndFoldersSetReturnsTrue()
    {
        var vm = CreateViewModel();
        vm.IsBusy = false;
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        Assert.True(vm.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public void PrepareCommandCanExecuteWhenBusyReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = true;
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        Assert.False(vm.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public void PrepareCommandCanExecuteWhenMissingFolderReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = false;
        vm.RomFolderPath = "";
        vm.CoverFolderPath = @"C:\Covers";
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        Assert.False(vm.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public void PrepareCommandCanExecuteWhenNoSystemReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = false;
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.SelectedSystem = null;

        Assert.False(vm.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public void DownloadCommandCanExecuteWhenItemsExistAndNotBusyReturnsTrue()
    {
        var vm = CreateViewModel();
        vm.IsBusy = false;
        vm.ItemsToDownload.Add(new CoverDownloadItem { GameName = "Test" });

        Assert.True(vm.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public void DownloadCommandCanExecuteWhenNoItemsReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = false;

        Assert.False(vm.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public void DownloadCommandCanExecuteWhenBusyReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = true;
        vm.ItemsToDownload.Add(new CoverDownloadItem { GameName = "Test" });

        Assert.False(vm.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public void CancelCommandCanExecuteWhenBusyAndCtsSetReturnsTrue()
    {
        var vm = CreateViewModel();
        vm.IsBusy = true;
        SetCts(vm, new CancellationTokenSource());

        Assert.True(vm.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void CancelCommandCanExecuteWhenNotBusyReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = false;

        Assert.False(vm.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void CancelCommandCanExecuteWhenBusyButNoCtsReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.IsBusy = true;

        Assert.False(vm.CancelCommand.CanExecute(null));
    }

    #endregion

    #region PrepareDownloadAsync

    [Fact]
    public async Task PrepareDownloadAsyncMissingDirectoryLogsErrorAndSetsBusyFalse()
    {
        var vm = CreateViewModel();
        vm.DirectoryExistsOverride = static _ => false;
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        await vm.PrepareDownloadAsync();

        Assert.False(vm.IsBusy);
        Assert.Empty(vm.ItemsToDownload);
    }

    [Fact]
    public async Task PrepareDownloadAsyncNoMissingCoversLeavesItemsEmpty()
    {
        var fake = new FakeGitHubService();
        var vm = CreateViewModel(service: fake);
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.FilesByPath[vm.RomFolderPath] = [@"C:\Roms\Game1.nes"];
        vm.FilesByPath[vm.CoverFolderPath] = [@"C:\Covers\Game1.png"];
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        fake.OnGetSystemFilesAsync = static (_, _) => Task.FromResult(("main", new List<GitHubTreeItem>
        {
            new() { Path = "Named_Boxarts/Game1.png", Type = "blob" }
        }));

        await vm.PrepareDownloadAsync();

        Assert.Empty(vm.ItemsToDownload);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task PrepareDownloadAsyncMatchingCoversPopulatesItemsToDownload()
    {
        var fake = new FakeGitHubService();
        var vm = CreateViewModel(service: fake);
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.FilesByPath[vm.RomFolderPath] = [@"C:\Roms\Super Mario Bros.nes"];
        vm.FilesByPath[vm.CoverFolderPath] = [];
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        fake.OnGetSystemFilesAsync = static (_, _) => Task.FromResult(("main", new List<GitHubTreeItem>
        {
            new() { Path = "Named_Boxarts/Super Mario Bros.png", Type = "blob" }
        }));

        await vm.PrepareDownloadAsync();

        Assert.Single(vm.ItemsToDownload);
        Assert.Equal("Super Mario Bros", vm.ItemsToDownload[0].GameName);
        Assert.Equal("Super Mario Bros.png", vm.ItemsToDownload[0].TargetFilename);
        Assert.Contains("raw.githubusercontent.com", vm.ItemsToDownload[0].DownloadUrl);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task PrepareDownloadAsyncGitHubFilesEmptyLeavesItemsEmpty()
    {
        var fake = new FakeGitHubService();
        var vm = CreateViewModel(service: fake);
        vm.RomFolderPath = @"C:\Roms";
        vm.CoverFolderPath = @"C:\Covers";
        vm.FilesByPath[vm.RomFolderPath] = [@"C:\Roms\Game1.nes"];
        vm.FilesByPath[vm.CoverFolderPath] = [];
        vm.SelectedSystem = new SystemConfig("NES", "owner", "repo", "Named_Boxarts");

        fake.OnGetSystemFilesAsync = static (_, _) => Task.FromResult(("main", new List<GitHubTreeItem>()));

        await vm.PrepareDownloadAsync();

        Assert.Empty(vm.ItemsToDownload);
        Assert.False(vm.IsBusy);
    }

    #endregion

    #region DownloadCoversAsync

    [Fact]
    public async Task DownloadCoversAsyncNoItemsReturnsImmediately()
    {
        var vm = CreateViewModel();

        await vm.DownloadCoversAsync();

        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task DownloadCoversAsyncSuccessfulDownloadWritesFilesAndUpdatesProgress()
    {
        var fake = new FakeGitHubService();
        var vm = CreateViewModel(service: fake);
        vm.CoverFolderPath = @"C:\Covers";
        vm.ItemsToDownload.Add(new CoverDownloadItem
        {
            GameName = "Super Mario Bros",
            TargetFilename = "Super Mario Bros.png",
            DownloadUrl = "http://example.com/cover.png"
        });

        fake.OnDownloadFileAsync = static (_, _) => Task.FromResult<byte[]?>([1, 2, 3]);

        await vm.DownloadCoversAsync();

        Assert.Single(vm.WrittenFiles);
        Assert.Equal(new byte[] { 1, 2, 3 }, vm.WrittenFiles[@"C:\Covers\Super Mario Bros.png"]);
        Assert.Equal(1, vm.ProgressValue);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task DownloadCoversAsyncLowDiskSpaceAbortsWithIoException()
    {
        var fake = new FakeGitHubService();
        var vm = CreateViewModel(service: fake);
        vm.CoverFolderPath = @"C:\Covers";
        vm.AvailableFreeSpace = 0;
        vm.ItemsToDownload.Add(new CoverDownloadItem
        {
            GameName = "Super Mario Bros",
            TargetFilename = "Super Mario Bros.png",
            DownloadUrl = "http://example.com/cover.png"
        });

        fake.OnDownloadFileAsync = static (_, _) => Task.FromResult<byte[]?>([1, 2, 3]);

        await vm.DownloadCoversAsync();

        Assert.False(vm.IsBusy);
        Assert.Empty(vm.WrittenFiles);
    }

    [Fact]
    public async Task DownloadCoversAsyncNullDataSkipsItem()
    {
        var fake = new FakeGitHubService();
        var vm = CreateViewModel(service: fake);
        vm.CoverFolderPath = @"C:\Covers";
        vm.ItemsToDownload.Add(new CoverDownloadItem
        {
            GameName = "Super Mario Bros",
            TargetFilename = "Super Mario Bros.png",
            DownloadUrl = "http://example.com/cover.png"
        });

        fake.OnDownloadFileAsync = static (_, _) => Task.FromResult<byte[]?>(null);

        await vm.DownloadCoversAsync();

        Assert.Empty(vm.WrittenFiles);
        Assert.Equal(1, vm.ProgressValue);
        Assert.False(vm.IsBusy);
    }

    #endregion

    #region Disposal

    [Fact]
    public void DisposeCallsGitHubServiceDispose()
    {
        var fake = new FakeGitHubService();
        var vm = CreateViewModel(service: fake);

        vm.Dispose();

        Assert.True(fake.Disposed);
    }

    [Fact]
    public void DisposeMultipleCallsDoesNotThrow()
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
    public void UpdateTokenSwapsServiceAndDoesNotDisposeImmediately()
    {
        var oldService = new FakeGitHubService();
        var newService = new FakeGitHubService();
        var tempPath = CreateTempSettingsFile();
        var vm = new TestableMainViewModelWithFactory(oldService, newService, tempPath);

        try
        {
            vm.UpdateToken("new-token");

            Assert.False(oldService.Disposed, "Old service should NOT be disposed immediately to avoid race with in-flight operations.");
            Assert.Contains("token updated", vm.LogText, StringComparison.OrdinalIgnoreCase);

            vm.Dispose();
            Assert.True(oldService.Disposed, "Old service should be disposed when ViewModel is disposed (drained from orphan queue).");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void UpdateProxySettingsSwapsServiceAndDoesNotDisposeImmediately()
    {
        var oldService = new FakeGitHubService();
        var newService = new FakeGitHubService();
        var vm = new TestableMainViewModelWithFactory(oldService, newService);

        vm.UpdateProxySettings(true, "proxy", 8080, "user", "pass");

        Assert.False(oldService.Disposed, "Old service should NOT be disposed immediately to avoid race with in-flight operations.");
        Assert.Contains("Proxy settings updated", vm.LogText, StringComparison.OrdinalIgnoreCase);

        vm.Dispose();
        Assert.True(oldService.Disposed, "Old service should be disposed when ViewModel is disposed (drained from orphan queue).");
    }

    private class TestableMainViewModelWithFactory : MainViewModel
    {
        private readonly FakeGitHubService _next;
        private readonly string _settingsFilePath;

        public TestableMainViewModelWithFactory(IGitHubService initial, FakeGitHubService next, string? settingsFilePath = null)
            : base(new AppSettings(), initial, true)
        {
            _next = next;
            _settingsFilePath = settingsFilePath ?? SettingsManager.DefaultSettingsFilePath;
        }

        // ReSharper disable once FunctionRecursiveOnAllPaths
        // ReSharper disable once ConvertToAutoProperty
        protected override string SettingsFilePath => _settingsFilePath;

        protected override IGitHubService CreateGitHubService(string? token, bool useProxy, string? proxyHost, int proxyPort, string? proxyUsername, string? proxyPassword)
        {
            return _next;
        }
    }

    #endregion
}
