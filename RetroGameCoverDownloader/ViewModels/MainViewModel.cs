using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using RetroGameCoverDownloader.Commands;
using RetroGameCoverDownloader.Managers;
using RetroGameCoverDownloader.Models;
using RetroGameCoverDownloader.Services;
using Application = System.Windows.Application;

namespace RetroGameCoverDownloader.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private IGitHubService _gitHubService;
    private CancellationTokenSource? _cts;
    private readonly DispatcherTimer _countdownTimer; // Timer for UI updates
    private TimeSpan _remainingWaitTime;
    private string? _currentToken;

    // Data
    public ObservableCollection<SystemConfig> Systems { get; } = [];
    internal readonly List<CoverDownloadItem> ItemsToDownload = [];
    private readonly StringBuilder _logBuilder = new();

    // Commands
    public RelayCommand BrowseRomCommand { get; }
    public RelayCommand BrowseCoverCommand { get; }
    public RelayCommand PrepareCommand { get; }
    public RelayCommand DownloadCommand { get; }
    public RelayCommand CancelCommand { get; }

    // 1. Add a property for the UI message
    public string StatusMessage
    {
        get;
        set => SetField(ref field, value);
    } = "Ready";

    public MainViewModel() : this(LoadSettingsSafe(), null, false)
    {
    }

    private static AppSettings LoadSettingsSafe()
    {
        try
        {
            return SettingsManager.LoadSettings();
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "[MainViewModel] Constructor failed to load settings.");
            return new AppSettings();
        }
    }

    internal MainViewModel(AppSettings settings, IGitHubService? gitHubService, bool suppressStartup)
    {
        _currentToken = settings.GitHubToken;

        // Initialize the timer first so Log is safe
        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += OnTimerTick;

        _gitHubService = gitHubService ?? CreateGitHubService(
            settings.GitHubToken,
            settings.UseProxy,
            settings.ProxyHost,
            settings.ProxyPort,
            settings.ProxyUsername,
            settings.ProxyPassword);

        try
        {
            // Subscribe to the Rate Limit event
            _gitHubService.RateLimitHit += OnRateLimitHit;
        }
        catch (Exception ex)
        {
            Log($"[MainViewModel] Failed to subscribe to rate limit events: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[MainViewModel] Constructor failed to subscribe to rate limit events.");
        }

        // Init Commands
        BrowseRomCommand = new RelayCommand(_ => SelectFolder(path => { RomFolderPath = path; }));
        BrowseCoverCommand = new RelayCommand(_ => SelectFolder(path => { CoverFolderPath = path; }));

        PrepareCommand = new RelayCommand(async void (o) =>
        {
            try
            {
                await PrepareDownloadAsync();
            }
            catch (Exception ex)
            {
                Log($"[PrepareCommand] Error: {ex.Message}");
                _ = BugReportService.LogErrorAsync(ex, "[PrepareCommand] Unhandled exception in PrepareCommand execution.");
            }
        }, _ => !IsBusy && SelectedSystem != null && !string.IsNullOrEmpty(RomFolderPath) && !string.IsNullOrEmpty(CoverFolderPath));
        DownloadCommand = new RelayCommand(async void (o) =>
        {
            try
            {
                await DownloadCoversAsync();
            }
            catch (Exception ex)
            {
                Log($"[DownloadCommand] Error: {ex.Message}");
                _ = BugReportService.LogErrorAsync(ex, "[DownloadCommand] Unhandled exception in DownloadCommand execution.");
            }
        }, _ => !IsBusy && ItemsToDownload.Count > 0);
        CancelCommand = new RelayCommand(
            _ => CancelOperation(),
            _ => IsBusy && _cts != null // Add null check to disable button sooner
        );

        if (!suppressStartup)
        {
            // Load Systems on Startup
            try
            {
                _ = LoadSystemsAsync();
            }
            catch (Exception ex)
            {
                Log($"[LoadSystemsAsync] Error: {ex.Message}");
                _ = BugReportService.LogErrorAsync(ex, "[LoadSystemsAsync] An error occurred while loading systems from GitHub.");
            }

            // Check for updates
            _ = UpdateCheckerService.CheckForUpdateAsync();
        }
    }

    // 5. Handle the timer tick
    private void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            _remainingWaitTime = _remainingWaitTime.Subtract(TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            Log($"[OnTimerTick] Error updating countdown: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[OnTimerTick] Exception in countdown timer tick.");
            _countdownTimer.Stop();
            return;
        }

        if (_remainingWaitTime <= TimeSpan.Zero)
        {
            _countdownTimer.Stop();
            StatusMessage = "Resuming downloads...";
        }
        else
        {
            StatusMessage = $"Rate limit reached. Resuming in {_remainingWaitTime.TotalSeconds:F0} seconds...";
        }
    }

    // Method to update token at runtime without restart
    public void UpdateToken(string token)
    {
        try
        {
            // Load current settings to preserve proxy configuration
            var settings = SettingsManager.LoadSettings();

            // Create new service before disposing old one to avoid leaving _gitHubService in a disposed state
            var newService = CreateGitHubService(
                token,
                settings.UseProxy,
                settings.ProxyHost,
                settings.ProxyPort,
                settings.ProxyUsername,
                settings.ProxyPassword);

            // Cleanup old service
            _gitHubService.RateLimitHit -= OnRateLimitHit;
            _gitHubService.Dispose();

            // Swap in new service
            _gitHubService = newService;
            _gitHubService.RateLimitHit += OnRateLimitHit;

            // Store the current token for future proxy updates
            _currentToken = token;

            Log("[MainViewModel] GitHub token updated. Rate limits increased.");
        }
        catch (Exception ex)
        {
            Log($"[UpdateToken] Error updating service: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[MainViewModel] Failed to update token at runtime.");
        }
    }

    // Method to update proxy settings and recreate the GitHubService
    public void UpdateProxySettings(bool useProxy, string? proxyHost, int proxyPort, string? proxyUsername, string? proxyPassword)
    {
        try
        {
            // Create new service before disposing old one to avoid leaving _gitHubService in a disposed state
            var newService = CreateGitHubService(
                _currentToken,
                useProxy,
                proxyHost,
                proxyPort,
                proxyUsername,
                proxyPassword);

            // Cleanup old service
            _gitHubService.RateLimitHit -= OnRateLimitHit;
            _gitHubService.Dispose();

            // Swap in new service
            _gitHubService = newService;
            _gitHubService.RateLimitHit += OnRateLimitHit;

            var proxyStatus = AppSettings.FormatProxyStatus(useProxy, proxyHost, proxyPort);
            Log($"[MainViewModel] Proxy settings updated. Proxy: {proxyStatus}");
        }
        catch (Exception ex)
        {
            Log($"[UpdateProxySettings] Error updating service: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[MainViewModel] Failed to update proxy settings at runtime.");
        }
    }

    // 4. Handle the event from RateLimiter
    private void OnRateLimitHit(TimeSpan waitTime)
    {
        try
        {
            _remainingWaitTime = waitTime;

            // Ensure we update UI on the UI thread
            InvokeOnDispatcher(() =>
            {
                StatusMessage = $"Rate limit reached. Resuming in {_remainingWaitTime.TotalSeconds:F0} seconds...";
                _countdownTimer.Start();
            });
        }
        catch (Exception ex)
        {
            Log($"[OnRateLimitHit] Error handling rate limit: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[OnRateLimitHit] Exception while processing rate limit hit event.");
        }
    }

    // Properties
    public string RomFolderPath
    {
        get;
        set => SetField(ref field, value);
    } = "";

    public string CoverFolderPath
    {
        get;
        set => SetField(ref field, value);
    } = "";

    public SystemConfig? SelectedSystem
    {
        get;
        set => SetField(ref field, value);
    }

    public string LogText
    {
        get;
        set => SetField(ref field, value);
    } = "";

    public bool IsBusy
    {
        get;
        set
        {
            if (SetField(ref field, value))
                InvalidateCommands();
        }
    }

    public int ProgressValue
    {
        get;
        set => SetField(ref field, value);
    }

    public int ProgressMax
    {
        get;
        set => SetField(ref field, value);
    } = 100;

    // Logic
    public void Log(string message)
    {
        try
        {
            _logBuilder.AppendLine(CultureInfo.InvariantCulture, $"[{DateTime.Now:HH:mm:ss}] {message}");

            // Cap log size to prevent unbounded memory growth
            const int maxLogLength = 100_000;
            if (_logBuilder.Length > maxLogLength)
            {
                var text = _logBuilder.ToString();
                text = text[^maxLogLength..];
                var firstNewLine = text.IndexOf(Environment.NewLine, StringComparison.Ordinal);
                if (firstNewLine >= 0)
                {
                    text = text.Substring(firstNewLine + Environment.NewLine.Length);
                }

                _logBuilder.Clear();
                _logBuilder.Append(text);
            }

            LogText = _logBuilder.ToString();
        }
        catch (Exception ex)
        {
            // If logging fails, try to report it but don't throw
            _ = BugReportService.LogErrorAsync(ex, $"[Log] Failed to append log message: {message}");
        }

        // Optional: Update the status message for normal logs too, if not waiting
        if (!_countdownTimer.IsEnabled)
        {
            StatusMessage = message;
        }
    }

    private async Task LoadSystemsAsync()
    {
        try
        {
            IsBusy = true;
            Log("Loading available systems from GitHub...");

            try
            {
                var systems = await Task.Run(() => _gitHubService.GetAvailableSystemsAsync(Log));

                InvokeOnDispatcher(() =>
                {
                    try
                    {
                        Systems.Clear();
                        foreach (var sys in systems.OrderBy(static s => s.SystemName)) Systems.Add(sys);
                    }
                    catch (Exception ex)
                    {
                        Log($"[LoadSystemsAsync] Error updating UI: {ex.Message}");
                        _ = BugReportService.LogErrorAsync(ex, "[LoadSystemsAsync] Exception while updating systems collection in UI.");
                    }
                });

                Log($"Loaded {systems.Count} systems.");
            }
            catch (Exception ex)
            {
                Log($"[LoadSystemsAsync] Error: {ex.Message}");
                _ = BugReportService.LogErrorAsync(ex, "[LoadSystemsAsync] An error occurred while loading systems from GitHub.");
            }
            finally
            {
                try
                {
                    IsBusy = false;
                }
                catch (Exception ex)
                {
                    Log($"[LoadSystemsAsync] Error resetting busy state: {ex.Message}");
                    _ = BugReportService.LogErrorAsync(ex, "[LoadSystemsAsync] Exception in finally block while resetting IsBusy.");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[LoadSystemsAsync] Generic error: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[LoadSystemsAsync] Generic error.");
        }
    }

    protected virtual void SelectFolder(Action<string> setPath)
    {
        try
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                setPath(dialog.SelectedPath);
            }
        }
        catch (Exception ex)
        {
            Log($"[SelectFolder] Error: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[SelectFolder] Exception while showing folder browser dialog.");
        }
    }

    internal async Task PrepareDownloadAsync()
    {
        if (SelectedSystem == null) return;

        IsBusy = true;
        ItemsToDownload.Clear();
        Log("--- Starting Preparation ---");

        try
        {
            await Task.Run(async () =>
            {
                // 1. Validate folders exist before reading
                if (!DirectoryExists(RomFolderPath) || !DirectoryExists(CoverFolderPath))
                {
                    const string errorMsg = "ROM folder or Cover folder does not exist.";
                    Log($"[PrepareDownloadAsync] {errorMsg}");
                    throw new DirectoryNotFoundException(errorMsg);
                }

                // 2. Scan ROMs
                Log($"Scanning ROM folder: {RomFolderPath}");
                var romFiles = GetFiles(RomFolderPath);
                var romNames = romFiles.Select(static f => Path.GetFileNameWithoutExtension(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Log($"Found {romNames.Count} ROMs.");

                // 3. Scan Covers
                Log($"Scanning Cover folder: {CoverFolderPath}");
                var coverFiles = GetFiles(CoverFolderPath);
                var coverNames = coverFiles.Select(static f => Path.GetFileNameWithoutExtension(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Log($"Found {coverNames.Count} existing covers.");

                // 3. Identify Missing
                var missingCovers = romNames.Where(r => !coverNames.Contains(r)).ToList();
                Log($"Missing {missingCovers.Count} covers based on local files.");

                if (missingCovers.Count == 0)
                {
                    Log("No covers missing. Nothing to do.");
                    return;
                }

                // 4. Fetch GitHub List
                Log($"Fetching file list from GitHub for {SelectedSystem.SystemName}...");
                var (branch, githubFiles) = await _gitHubService.GetSystemFilesAsync(SelectedSystem, Log);

                if (githubFiles.Count == 0)
                {
                    Log($"[PrepareDownloadAsync] No files found for {SelectedSystem.SystemName}.");
                    return;
                }

                Log($"Found {githubFiles.Count} files in repository (Branch: {branch}).");

                // 5. Match Missing vs GitHub
                // GitHub paths are like "Named_Boxarts/Game Name.png"
                // We match "Game Name"
                foreach (var missing in missingCovers)
                {
                    // Find a file in GitHub list where filename (no ext) matches missing ROM name
                    var match = githubFiles.FirstOrDefault(g =>
                        string.Equals(Path.GetFileNameWithoutExtension(g.Path), missing, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        var fileName = Path.GetFileName(match.Path);
                        // Construct raw URL using the detected branch.
                        // Encode each path segment separately to preserve '/' separators.
                        var encodedPath = string.Join("/", match.Path.Split('/').Select(Uri.EscapeDataString));
                        var url = $"https://raw.githubusercontent.com/{SelectedSystem.Owner}/{SelectedSystem.Repo}/{branch}/{encodedPath}";

                        ItemsToDownload.Add(new CoverDownloadItem
                        {
                            GameName = missing,
                            TargetFilename = fileName,
                            DownloadUrl = url
                        });
                    }
                }

                Log($"Matched {ItemsToDownload.Count} covers available for download.");
            }, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            Log("Preparation cancelled.");
            _ = BugReportService.LogErrorAsync(new OperationCanceledException("Preparation cancelled by user."), "[PrepareDownloadAsync] Operation was cancelled.");
        }
        catch (UnauthorizedAccessException ex)
        {
            Log($"[PrepareDownloadAsync] Access denied to folder: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[PrepareDownloadAsync] Unauthorized access to ROM or Cover folder.");
        }
        catch (Exception ex)
        {
            Log($"Error during preparation: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[PrepareDownloadAsync] An error occurred during the preparation task.");
        }
        finally
        {
            try
            {
                IsBusy = false;
                _countdownTimer.Stop();
                // Force command refresh to enable Download button
                InvalidateCommands();
            }
            catch (Exception ex)
            {
                Log($"[PrepareDownloadAsync] Error in finally block: {ex.Message}");
                _ = BugReportService.LogErrorAsync(ex, "[PrepareDownloadAsync] Exception in finally block while resetting state.");
            }
        }
    }

    internal async Task DownloadCoversAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();

        // Validate we have items to download
        var token = _cts.Token;

        ProgressMax = ItemsToDownload.Count;
        ProgressValue = 0;
        var successCount = 0;

        Log("--- Starting Download ---");

        if (ItemsToDownload.Count == 0)
        {
            Log("[DownloadCoversAsync] No items to download.");
            IsBusy = false;
            return;
        }

        try
        {
            foreach (var item in ItemsToDownload)
            {
                if (token.IsCancellationRequested)
                {
                    Log("Download cancelled by user.");
                    break;
                }

                // Validate download URL
                if (string.IsNullOrWhiteSpace(item.DownloadUrl))
                {
                    Log($"[DownloadCoversAsync] Invalid download URL for {item.GameName}. Skipping.");
                    continue;
                }

                Log($"Downloading: {item.GameName}...");

                // Feature 2: Pass log callback to show "Retrying..." messages in UI automatically
                var data = await _gitHubService.DownloadFileAsync(item.DownloadUrl, Log, token);

                if (data != null)
                {
                    // Check for disk space before writing
                    if (GetAvailableFreeSpace(CoverFolderPath) < 10 * 1024 * 1024) // 10MB threshold
                    {
                        throw new IOException("Low disk space detected. Download aborted.");
                    }

                    var savePath = Path.Combine(CoverFolderPath, item.TargetFilename);
                    await WriteAllBytesAsync(savePath, data, token);

                    // Verify file was written
                    if (!FileExists(savePath))
                    {
                        throw new IOException($"File was not created at {savePath}");
                    }

                    successCount++;
                }
                else
                {
                    Log($"Failed to download {item.GameName}");
                }

                ProgressValue++;
            }
        }
        catch (OperationCanceledException)
        {
            Log("Download cancelled by user.");
            _ = BugReportService.LogErrorAsync(new OperationCanceledException("Download cancelled by user."), "[DownloadCoversAsync] Operation was cancelled.");
        }
        catch (IOException ex)
        {
            Log($"[DownloadCoversAsync] File I/O error: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[DownloadCoversAsync] IOException during file operations.");
        }
        catch (Exception ex)
        {
            Log($"Error during download batch: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[DownloadCoversAsync] An error occurred during the download batch task.");
        }
        finally
        {
            try
            {
                Log($"Download finished. Successfully saved {successCount} covers.");
                StatusMessage = $"Download complete. Saved {successCount} covers.";
                _countdownTimer.Stop();
                IsBusy = false;
            }
            catch (Exception ex)
            {
                Log($"[DownloadCoversAsync] Error in finally block: {ex.Message}");
                _ = BugReportService.LogErrorAsync(ex, "[DownloadCoversAsync] Exception in finally block while cleaning up.");
            }
            finally
            {
                // Dispose and null out in a nested finally to ensure it always happens
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    private void CancelOperation()
    {
        // Capture the token source locally to avoid TOCTOU race with DownloadCoversAsync
        var cts = Interlocked.CompareExchange(ref _cts, null, null);
        if (cts == null)
        {
            Log("No active operation to cancel.");
            return;
        }

        try
        {
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
                Log("Cancellation requested...");
            }
        }
        catch (ObjectDisposedException)
        {
            Log("Cancellation requested but operation already completed.");
        }
        catch (Exception ex)
        {
            Log($"[CancelOperation] Error cancelling operation: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[CancelOperation] Exception while cancelling operation.");
        }

        // Note: We do NOT dispose or null _cts here.
        // The worker task (DownloadCoversAsync) owns the lifecycle and will dispose/null it
        // in its finally block. Doing it here causes race conditions.
    }

    public void CancelAll()
    {
        try
        {
            _countdownTimer.Stop();

            var cts = Interlocked.CompareExchange(ref _cts, null, null);
            if (cts is { IsCancellationRequested: false })
            {
                cts.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
            // Ignore
        }
        catch (Exception ex)
        {
            Log($"[CancelAll] Error: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[MainViewModel] Error cancelling all operations.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            _countdownTimer.Stop();
            _countdownTimer.Tick -= OnTimerTick;
        }
        catch
        {
            /* ignore */
        }

        try
        {
            _gitHubService.RateLimitHit -= OnRateLimitHit;
        }
        catch
        {
            /* ignore */
        }

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        catch
        {
            /* ignore */
        }

        try
        {
            _gitHubService.Dispose();
        }
        catch
        {
            /* ignore */
        }

        GC.SuppressFinalize(this);
    }

    protected virtual void InvokeOnDispatcher(Action action)
    {
        Application.Current.Dispatcher.Invoke(action);
    }

    protected virtual void InvalidateCommands()
    {
        CommandManager.InvalidateRequerySuggested();
    }

    protected virtual bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    protected virtual string[] GetFiles(string path)
    {
        return Directory.GetFiles(path);
    }

    protected virtual Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken)
    {
        return File.WriteAllBytesAsync(path, data, cancellationToken);
    }

    protected virtual bool FileExists(string path)
    {
        return File.Exists(path);
    }

    protected virtual long GetAvailableFreeSpace(string path)
    {
        var root = Path.GetPathRoot(path) ?? throw new InvalidOperationException("Could not get root path of Cover folder");
        return new DriveInfo(root).AvailableFreeSpace;
    }

    protected virtual IGitHubService CreateGitHubService(string? token, bool useProxy, string? proxyHost, int proxyPort, string? proxyUsername, string? proxyPassword)
    {
        return new GitHubService(token, useProxy, proxyHost, proxyPort, proxyUsername, proxyPassword);
    }

    private bool _disposed;
}