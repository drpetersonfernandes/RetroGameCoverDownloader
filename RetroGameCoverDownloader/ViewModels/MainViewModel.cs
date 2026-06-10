using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using RetroGameCoverDownloader.Commands;
using RetroGameCoverDownloader.Helpers;
using RetroGameCoverDownloader.Managers;
using RetroGameCoverDownloader.Models;
using RetroGameCoverDownloader.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace RetroGameCoverDownloader.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private IGitHubService _gitHubService;
    private CancellationTokenSource? _cts;
    private readonly DispatcherTimer _countdownTimer; // Timer for UI updates
    private TimeSpan _remainingWaitTime;
    private string? _currentToken;
    private readonly ConcurrentQueue<IGitHubService> _orphanedServices = new();

    internal bool HasGitHubToken => !string.IsNullOrWhiteSpace(_currentToken);
    internal volatile HashSet<string> FileExtensions = new(StringComparer.OrdinalIgnoreCase);

    // Data
    // ReSharper disable once CollectionNeverQueried.Global
    public ObservableCollection<SystemConfig> Systems { get; } = [];
    internal readonly List<CoverDownloadItem> ItemsToDownload = [];
    private readonly StringBuilder _logBuilder = new();

    // Commands
    public RelayCommand BrowseRomCommand { get; }
    public RelayCommand BrowseCoverCommand { get; }
    public RelayCommand PrepareCommand { get; }
    public RelayCommand DownloadCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenUpdateUrlCommand { get; }
    public RelayCommand DismissUpdateCommand { get; }
    public RelayCommand CheckForUpdatesCommand { get; }

    // Update notification
    public bool UpdateAvailable
    {
        get;
        set => SetField(ref field, value);
    }

    public string UpdateVersionText
    {
        get;
        set => SetField(ref field, value);
    } = "";

    public string UpdateReleaseUrl
    {
        get;
        set => SetField(ref field, value);
    } = "";

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
            return SettingsManager.LoadSettings(SettingsManager.DefaultSettingsFilePath);
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "Constructor failed to load settings.");
            return new AppSettings();
        }
    }

    protected virtual string SettingsFilePath => SettingsManager.DefaultSettingsFilePath;

    internal MainViewModel(AppSettings settings, IGitHubService? gitHubService, bool suppressStartup)
    {
        _currentToken = settings.GitHubToken;

        // Load file extensions from settings
        if (settings.FileExtensions.Count > 0)
        {
            FileExtensions = new HashSet<string>(settings.FileExtensions, StringComparer.OrdinalIgnoreCase);
        }

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
            _gitHubService.UnauthorizedAccess += OnUnauthorizedAccess;
        }
        catch (Exception ex)
        {
            Log($"[MainViewModel] Failed to subscribe to rate limit events: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Constructor failed to subscribe to rate limit events.");
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
                Log($"[MainViewModel.PrepareCommand] Error: {ex.Message}");
                _ = BugReportService.LogErrorAsync(ex, "[MainViewModel.PrepareCommand] Unhandled exception in PrepareCommand execution.");
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
                Log($"[MainViewModel.DownloadCommand] Error: {ex.Message}");
                _ = BugReportService.LogErrorAsync(ex, "[MainViewModel.DownloadCommand] Unhandled exception in DownloadCommand execution.");
            }
        }, _ => !IsBusy && ItemsToDownload.Count > 0);
        CancelCommand = new RelayCommand(
            _ => CancelOperation(),
            _ => IsBusy && _cts != null // Add null check to disable button sooner
        );

        OpenUpdateUrlCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrEmpty(UpdateReleaseUrl))
                UpdateCheckerService.OpenUrlInBrowser(UpdateReleaseUrl);
        });

        DismissUpdateCommand = new RelayCommand(_ =>
        {
            UpdateAvailable = false;
        });

        CheckForUpdatesCommand = new RelayCommand(async void (_) =>
        {
            try
            {
                Log("Checking for updates...");
                await UpdateCheckerService.CheckForUpdateAsync(Log);
            }
            catch (Exception ex)
            {
                Log($"[MainViewModel.CheckForUpdatesCommand] Error: {ex.Message}");
                await BugReportService.LogErrorAsync(ex, "Error checking for updates.");
            }
        });

        UpdateCheckerService.UpdateAvailable += OnUpdateAvailable;

        if (!suppressStartup)
        {
            // Load Systems on Startup
            try
            {
                _ = LoadSystemsAsync();
            }
            catch (Exception ex)
            {
                Log($"[MainViewModel.LoadSystemsAsync] Error: {ex.Message}");
                _ = BugReportService.LogErrorAsync(ex, "An error occurred while loading systems from GitHub.");
            }

            // Check for updates
            _ = UpdateCheckerService.CheckForUpdateAsync(Log);
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
            Log($"[MainViewModel.OnTimerTick] Error updating countdown: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Exception in countdown timer tick.");
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
            var settings = SettingsManager.LoadSettings(SettingsFilePath);

            // Create new service before disposing old one to avoid leaving _gitHubService in a disposed state
            var newService = CreateGitHubService(
                token,
                settings.UseProxy,
                settings.ProxyHost,
                settings.ProxyPort,
                settings.ProxyUsername,
                settings.ProxyPassword);

            // Unsubscribe from old service and swap atomically
            _gitHubService.RateLimitHit -= OnRateLimitHit;
            _gitHubService.UnauthorizedAccess -= OnUnauthorizedAccess;
            var oldService = Interlocked.Exchange(ref _gitHubService, newService);
            _orphanedServices.Enqueue(oldService);
            newService.RateLimitHit += OnRateLimitHit;
            newService.UnauthorizedAccess += OnUnauthorizedAccess;

            // Store the current token for future proxy updates
            _currentToken = token;

            Log("[MainViewModel.UpdateToken] GitHub token updated. Rate limits increased.");
        }
        catch (Exception ex)
        {
            Log($"[MainViewModel.UpdateToken] Error updating service: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Failed to update token at runtime.");
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

            // Unsubscribe from old service and swap atomically
            _gitHubService.RateLimitHit -= OnRateLimitHit;
            _gitHubService.UnauthorizedAccess -= OnUnauthorizedAccess;
            var oldService = Interlocked.Exchange(ref _gitHubService, newService);
            _orphanedServices.Enqueue(oldService);
            newService.RateLimitHit += OnRateLimitHit;
            newService.UnauthorizedAccess += OnUnauthorizedAccess;

            var proxyStatus = AppSettings.FormatProxyStatus(useProxy, proxyHost, proxyPort);
            Log($"[MainViewModel.UpdateProxySettings] Proxy settings updated. Proxy: {proxyStatus}");
        }
        catch (Exception ex)
        {
            Log($"[MainViewModel.UpdateProxySettings] Error updating service: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Failed to update proxy settings at runtime.");
        }
    }

    public void UpdateFileExtensions(List<string> extensions)
    {
        FileExtensions = extensions.Count > 0
            ? new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Log($"[MainViewModel.UpdateFileExtensions] File extension filter updated. {(extensions.Count > 0 ? $"Filtering by {extensions.Count} extension(s)." : "No filter applied.")}");
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
            Log($"[MainViewModel.OnRateLimitHit] Error handling rate limit: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Exception while processing rate limit hit event.");
        }
    }

    private void OnUnauthorizedAccess()
    {
        try
        {
            InvokeOnDispatcher(() =>
            {
                try
                {
                    Log("[OnUnauthorizedAccess] GitHub returned 401 Unauthorized. Prompting for token...");
                    MessageBox.Show(
                        "GitHub returned a 401 Unauthorized error.\n\n" +
                        "Your GitHub token may be missing, invalid, or expired.\n" +
                        "Please enter a valid Personal Access Token to continue.",
                        "GitHub Authentication Required",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);

                    var dialog = new Views.TokenDialog(HasGitHubToken) { Owner = Application.Current.MainWindow };
                    if (dialog.ShowDialog() == true)
                    {
                        if (!string.IsNullOrEmpty(dialog.Token))
                        {
                            var settings = SettingsManager.LoadSettings();
                            settings.GitHubToken = dialog.Token;
                            SettingsManager.SaveSettings(settings);

                            UpdateToken(dialog.Token);
                            Log("[OnUnauthorizedAccess] New GitHub token saved and applied.");
                        }
                        else
                        {
                            Log("[OnUnauthorizedAccess] No new token provided. Continuing with limited access.");
                        }
                    }
                    else
                    {
                        Log("[OnUnauthorizedAccess] Token dialog cancelled. Continuing with limited access.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[OnUnauthorizedAccess] Error handling unauthorized access: {ex.Message}");
                    _ = BugReportService.LogErrorAsync(ex, "Exception while handling unauthorized access event.");
                }
            });
        }
        catch (Exception ex)
        {
            Log($"[OnUnauthorizedAccess] Error dispatching to UI thread: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Exception dispatching unauthorized access to UI thread.");
        }
    }

    private void OnUpdateAvailable(UpdateInfo updateInfo)
    {
        try
        {
            InvokeOnDispatcher(() =>
            {
                UpdateVersionText = $"Version {updateInfo.LatestVersion} is available";
                UpdateReleaseUrl = updateInfo.ReleaseUrl;
                UpdateAvailable = true;
                Log($"Update available: {updateInfo.LatestVersion} (current: {AppInfo.Version})");
            });
        }
        catch (Exception ex)
        {
            Log($"[MainViewModel.OnUpdateAvailable] Error: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Exception while processing update notification.");
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

                // If the slice started in the middle of a surrogate pair,
                // skip the orphaned low surrogate
                if (text.Length > 0 && char.IsLowSurrogate(text[0]))
                {
                    text = text[1..];
                }

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
            _ = BugReportService.LogErrorAsync(ex, $"[MainViewModel.Log] Failed to append log message: {message}");
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
                        Log($"[MainViewModel.LoadSystemsAsync] Error updating UI: {ex.Message}");
                        _ = BugReportService.LogErrorAsync(ex, "Exception while updating systems collection in UI.");
                    }
                });

                Log($"Loaded {systems.Count} systems.");
            }
            catch (Exception ex)
            {
                Log($"[MainViewModel.LoadSystemsAsync] Error: {ex.Message}");
                _ = BugReportService.LogErrorAsync(ex, "An error occurred while loading systems from GitHub.");
            }
            finally
            {
                try
                {
                    IsBusy = false;
                }
                catch (Exception ex)
                {
                    Log($"[MainViewModel.LoadSystemsAsync] Error resetting busy state: {ex.Message}");
                    _ = BugReportService.LogErrorAsync(ex, "Exception in finally block while resetting IsBusy.");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[MainViewModel.LoadSystemsAsync] Generic error: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Generic error.");
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
            Log($"[MainViewModel.SelectFolder] Error: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Exception while showing folder browser dialog.");
        }
    }

    internal async Task PrepareDownloadAsync()
    {
        var selectedSystem = SelectedSystem;
        if (selectedSystem == null) return;
        if (IsBusy) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        ItemsToDownload.Clear();
        Log("--- Starting Preparation ---");

        try
        {
            await Task.Run(async () =>
            {
                // 1. Validate folders exist before reading
                if (!DirectoryExists(RomFolderPath) || !DirectoryExists(CoverFolderPath))
                {
                    var missingFolders = new List<string>();
                    if (!DirectoryExists(RomFolderPath)) missingFolders.Add($"ROM folder: {RomFolderPath}");
                    if (!DirectoryExists(CoverFolderPath)) missingFolders.Add($"Cover folder: {CoverFolderPath}");
                    var errorMsg = $"The following folders do not exist:\n{string.Join("\n", missingFolders)}";
                    Log($"[MainViewModel.PrepareDownloadAsync] {errorMsg}");
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

                // 4. Identify Missing
                var missingCovers = romNames.Where(r => !coverNames.Contains(r)).ToList();
                Log($"Missing {missingCovers.Count} covers based on local files.");

                if (missingCovers.Count == 0)
                {
                    Log("No covers missing. Nothing to do.");
                    return;
                }

                // 5. Fetch GitHub List
                Log($"Fetching file list from GitHub for {selectedSystem.SystemName}...");
                var (branch, githubFiles) = await _gitHubService.GetSystemFilesAsync(selectedSystem, Log, token);

                if (githubFiles.Count == 0)
                {
                    Log($"[MainViewModel.PrepareDownloadAsync] No files found for {selectedSystem.SystemName}.");
                    return;
                }

                Log($"Found {githubFiles.Count} files in repository (Branch: {branch}).");

                // Filter out .gitkeep and other non-cover dot-files
                githubFiles = githubFiles
                    .Where(static g =>
                    {
                        var name = Path.GetFileName(g.Path);
                        return !string.IsNullOrEmpty(name)
                               && !name.StartsWith('.')
                               && !string.IsNullOrEmpty(Path.GetFileNameWithoutExtension(name));
                    })
                    .ToList();

                Log($"After filtering non-cover files: {githubFiles.Count} files.");

                // 6. Match Missing vs GitHub
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
                        var url = $"https://raw.githubusercontent.com/{selectedSystem.Owner}/{selectedSystem.Repo}/{branch}/{encodedPath}";

                        ItemsToDownload.Add(new CoverDownloadItem
                        {
                            GameName = missing,
                            TargetFilename = fileName,
                            DownloadUrl = url
                        });
                    }
                }

                Log($"Matched {ItemsToDownload.Count} covers available for download.");
            }, token);
        }
        catch (OperationCanceledException)
        {
            Log("Preparation cancelled.");
        }
        catch (UnauthorizedAccessException ex)
        {
            Log($"[MainViewModel.PrepareDownloadAsync] Access denied to folder: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Access denied to folder during preparation.");
        }
        catch (DirectoryNotFoundException ex)
        {
            Log($"[MainViewModel.PrepareDownloadAsync] {ex.Message}");
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Log($"Error during preparation: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "An error occurred during the preparation task.");
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
                Log($"[MainViewModel.PrepareDownloadAsync] Error in finally block: {ex.Message}");
                _ = BugReportService.LogErrorAsync(ex, "Exception in finally block while resetting state.");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                DrainOrphanedServices();
            }
        }
    }

    internal async Task DownloadCoversAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();

        // Validate we have items to download
        var token = _cts.Token;

        ProgressValue = 0;
        var successCount = 0;

        Log("--- Starting Download ---");

        if (ItemsToDownload.Count == 0)
        {
            Log("[MainViewModel.DownloadCoversAsync] No items to download.");
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
            return;
        }

        // Pre-filter: count only items that need actual downloading
        var itemsToProcess = new List<CoverDownloadItem>();
        foreach (var item in ItemsToDownload)
        {
            if (string.IsNullOrWhiteSpace(item.DownloadUrl))
            {
                Log($"[MainViewModel.DownloadCoversAsync] Invalid download URL for {item.GameName}. Skipping.");
                continue;
            }

            var savePath = Path.Combine(CoverFolderPath, item.TargetFilename);
            if (FileExists(savePath))
            {
                Log($"[MainViewModel.DownloadCoversAsync] Cover already exists for {item.GameName}. Skipping.");
                continue;
            }

            itemsToProcess.Add(item);
        }

        ProgressMax = itemsToProcess.Count;
        Log($"--- {itemsToProcess.Count} items to download ---");

        try
        {
            foreach (var item in itemsToProcess)
            {
                if (token.IsCancellationRequested)
                {
                    Log("Download cancelled by user.");
                    break;
                }

                var savePath = Path.Combine(CoverFolderPath, item.TargetFilename);

                Log($"Downloading: {item.GameName}...");

                // Feature 2: Pass log callback to show "Retrying..." messages in UI automatically
                var data = await _gitHubService.DownloadFileAsync(item.DownloadUrl, Log, token);

                if (data != null)
                {
                    // Check for disk space before writing
                    if (GetAvailableFreeSpace(CoverFolderPath) < 10 * 1024 * 1024)
                    {
                        var freeSpace = GetAvailableFreeSpace(CoverFolderPath);
                        var freeSpaceMb = freeSpace / (1024.0 * 1024.0);
                        throw new IOException($"Low disk space detected. Only {freeSpaceMb.ToString("F1", CultureInfo.InvariantCulture)} MB available ({CoverFolderPath}). Download aborted.");
                    }

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
        }
        catch (IOException ex)
        {
            Log($"[MainViewModel.DownloadCoversAsync] File I/O error: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "File I/O error during download batch.");
        }
        catch (Exception ex)
        {
            Log($"Error during download batch: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "An error occurred during the download batch task.");
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
                Log($"[MainViewModel.DownloadCoversAsync] Error in finally block: {ex.Message}");
                _ = BugReportService.LogErrorAsync(ex, "Exception in finally block while cleaning up.");
            }
            finally
            {
                // Dispose and null out in a nested finally to ensure it always happens
                _cts?.Dispose();
                _cts = null;
                DrainOrphanedServices();
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
            Log($"[MainViewModel.CancelOperation] Error cancelling operation: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Exception while cancelling operation.");
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
            Log($"[MainViewModel.CancelAll] Error: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Error cancelling all operations.");
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
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "Error stopping countdown timer.");
        }

        try
        {
            _gitHubService.RateLimitHit -= OnRateLimitHit;
            _gitHubService.UnauthorizedAccess -= OnUnauthorizedAccess;
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "Error unsubscribing from rate limit events.");
        }

        try
        {
            UpdateCheckerService.UpdateAvailable -= OnUpdateAvailable;
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "Error unsubscribing from update checker events.");
        }

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "Error disposing cancellation token source.");
        }

        try
        {
            _gitHubService.Dispose();
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "Error disposing GitHub service.");
        }

        DrainOrphanedServices();

        GC.SuppressFinalize(this);
    }

    private void DrainOrphanedServices()
    {
        while (_orphanedServices.TryDequeue(out var service))
        {
            try
            {
                service.Dispose();
            }
            catch (Exception ex)
            {
                _ = BugReportService.LogErrorAsync(ex, "Error disposing orphaned GitHub service.");
            }
        }
    }

    protected virtual void InvokeOnDispatcher(Action action)
    {
        Application.Current?.Dispatcher.Invoke(action);
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
        var files = Directory.GetFiles(path);
        var extensions = FileExtensions;
        if (extensions.Count == 0) return files;

        return files.Where(f => extensions.Contains(Path.GetExtension(f))).ToArray();
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

        try
        {
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (ArgumentException ex)
        {
            throw new IOException($"Unable to determine free space for path '{path}'. The drive may be unavailable or the path may be a UNC path.", ex);
        }
    }

    protected virtual IGitHubService CreateGitHubService(string? token, bool useProxy, string? proxyHost, int proxyPort, string? proxyUsername, string? proxyPassword)
    {
        return new GitHubService(token, useProxy, proxyHost, proxyPort, proxyUsername, proxyPassword);
    }

    private bool _disposed;
}