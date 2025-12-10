using System.Collections.ObjectModel;
using System.IO;
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
    private readonly GitHubService _gitHubService;
    private CancellationTokenSource? _cts;
    private readonly DispatcherTimer _countdownTimer; // Timer for UI updates
    private TimeSpan _remainingWaitTime;

    // Data
    public ObservableCollection<SystemConfig> Systems { get; } = new();
    private readonly List<CoverDownloadItem> _itemsToDownload = new();

    // Commands
    public RelayCommand? BrowseRomCommand { get; }
    public RelayCommand? BrowseCoverCommand { get; }
    public RelayCommand PrepareCommand { get; }
    public RelayCommand DownloadCommand { get; }
    public RelayCommand CancelCommand { get; }

    // 1. Add a property for the UI message
    public string StatusMessage
    {
        get;
        set => SetField(ref field, value);
    } = "Ready";

    public MainViewModel()
    {
        // Load Settings
        AppSettings settings;
        try
        {
            settings = SettingsManager.LoadSettings();
        }
        catch (Exception ex)
        {
            settings = new AppSettings();
            Log($"[MainViewModel] Failed to load settings: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[MainViewModel] Constructor failed to load settings.");
        }

        // Token Check Logic
        if (string.IsNullOrWhiteSpace(settings.GitHubToken))
        {
            // In a real app, you might open a Dialog Window here.
            // For simplicity, we assume the View handles the initial prompt or we just init service without token.
        }

        _gitHubService = new GitHubService(settings.GitHubToken);

        try
        {
            // 2. Subscribe to the Rate Limit event
            _gitHubService.RateLimitHit += OnRateLimitHit;
        }
        catch (Exception ex)
        {
            Log($"[MainViewModel] Failed to subscribe to rate limit events: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[MainViewModel] Constructor failed to subscribe to rate limit events.");
        }

        // 3. Initialize the timer
        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += OnTimerTick;

        try
        {
            // Init Commands
            BrowseRomCommand = new RelayCommand(_ => SelectFolder(path => { RomFolderPath = path; }));
            BrowseCoverCommand = new RelayCommand(_ => SelectFolder(path => { CoverFolderPath = path; }));
        }
        catch (Exception ex)
        {
            Log($"[MainViewModel] Failed to initialize browse commands: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[MainViewModel] Constructor failed to initialize browse commands.");
        }

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
        }, _ => !IsBusy && _itemsToDownload.Count > 0);
        CancelCommand = new RelayCommand(
            _ => CancelOperation(),
            _ => IsBusy && _cts != null // Add null check to disable button sooner
        );

        // Load Systems on Startup
        LoadSystemsAsync();

        // Check for updates
        _ = UpdateCheckerService.CheckForUpdateAsync();
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

    // 4. Handle the event from RateLimiter
    private void OnRateLimitHit(TimeSpan waitTime)
    {
        try
        {
            _remainingWaitTime = waitTime;

            // Ensure we update UI on the UI thread
            Application.Current.Dispatcher.Invoke(() =>
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
            SetField(ref field, value);
            CommandManager.InvalidateRequerySuggested();
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
            LogText += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
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

    private async void LoadSystemsAsync()
    {
        try
        {
            IsBusy = true;
            Log("Loading available systems from GitHub...");

            try
            {
                var systems = await Task.Run(() => _gitHubService.GetAvailableSystemsAsync(Log));

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        Systems.Clear();
                        foreach (var sys in systems.OrderBy(s => s.SystemName)) Systems.Add(sys);
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

    private void SelectFolder(Action<string> setPath)
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

    private async Task PrepareDownloadAsync()
    {
        if (SelectedSystem == null) return;

        _itemsToDownload.Clear();

        IsBusy = true;
        _itemsToDownload.Clear();
        Log("--- Starting Preparation ---");

        try
        {
            await Task.Run(async () =>
            {
                // 1. Scan ROMs
                Log($"Scanning ROM folder: {RomFolderPath}");
                var romFiles = Directory.GetFiles(RomFolderPath);
                var romNames = romFiles.Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Log($"Found {romNames.Count} ROMs.");

                // 2. Scan Covers
                Log($"Scanning Cover folder: {CoverFolderPath}");
                var coverFiles = Directory.GetFiles(CoverFolderPath);
                var coverNames = coverFiles.Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Log($"Found {coverNames.Count} existing covers.");

                // Validate folders exist
                if (!Directory.Exists(RomFolderPath) || !Directory.Exists(CoverFolderPath))
                {
                    const string errorMsg = "ROM folder or Cover folder does not exist.";
                    Log($"[PrepareDownloadAsync] {errorMsg}");
                    throw new DirectoryNotFoundException(errorMsg);
                }

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
                var githubFiles = await _gitHubService.GetSystemFilesAsync(SelectedSystem, Log);

                if (githubFiles == null)
                {
                    Log($"[PrepareDownloadAsync] GetSystemFilesAsync returned null for {SelectedSystem.SystemName}.");
                    throw new InvalidOperationException($"Failed to retrieve file list for {SelectedSystem.SystemName}.");
                }

                Log($"Found {githubFiles.Count} files in repository.");


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
                        // Construct raw URL. Note: We need to know the branch.
                        // Simplified: assuming 'master' or 'main' based on what GetSystemFilesAsync found,
                        // but for raw download we construct it dynamically.
                        // Ideally GetSystemFilesAsync should return full URL or branch info.
                        // Here we reconstruct based on standard pattern:
                        var url = $"https://raw.githubusercontent.com/{SelectedSystem.Owner}/{SelectedSystem.Repo}/master/{Uri.EscapeDataString(match.Path)}";
                        // Note: If repo uses 'main', this might fail 404.
                        // Improvement: Update GitHubService to return full download URL in the TreeItem or separate model.
                        // For this example, we will try to detect branch in Service or just try both.
                        // Let's assume 'master' for now, or handle 404 in download.

                        // Actually, let's fix the URL construction.
                        // Since we don't know the branch here easily without passing it back,
                        // let's assume the service handles the URL or we try both.
                        // A better approach: The Service should return objects with the valid Raw Url.

                        // HACK for this example: We will try 'master', if 404, 'main' is handled in download logic?
                        // No, let's just use the URL construction logic from the original Program.cs
                        // In Program.cs, it detected the branch.

                        // Let's assume 'master' for libretro-thumbnails usually.

                        _itemsToDownload.Add(new CoverDownloadItem
                        {
                            GameName = missing,
                            TargetFilename = fileName,
                            DownloadUrl = url // This needs to be accurate.
                        });
                    }
                }

                Log($"Matched {_itemsToDownload.Count} covers available for download.");
            }, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            Log("Preparation cancelled.");
            _ = BugReportService.LogErrorAsync(new Exception("Preparation cancelled by user."), "[PrepareDownloadAsync] Operation was cancelled.");
        }
        catch (UnauthorizedAccessException ex)
        {
            Log($"[PrepareDownloadAsync] Access denied to folder: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[PrepareDownloadAsync] Unauthorized access to ROM or Cover folder.");
            // Re-throw to maintain existing behavior
            throw;
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
                // Force command refresh to enable Download button
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                Log($"[PrepareDownloadAsync] Error in finally block: {ex.Message}");
                _ = BugReportService.LogErrorAsync(ex, "[PrepareDownloadAsync] Exception in finally block while resetting state.");
            }
        }
    }

    private async Task DownloadCoversAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();

        // Validate we have items to download
        var token = _cts.Token;

        ProgressMax = _itemsToDownload.Count;
        ProgressValue = 0;
        var successCount = 0;

        Log("--- Starting Download ---");

        if (_itemsToDownload.Count == 0)
        {
            Log("[DownloadCoversAsync] No items to download.");
            IsBusy = false;
            return;
        }

        try
        {
            foreach (var item in _itemsToDownload)
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

                // Try downloading (handling branch 'master' vs 'main' issue simply by trying)
                var data = await _gitHubService.DownloadFileAsync(item.DownloadUrl);
                if (data == null)
                {
                    // Fallback to 'main' if 'master' failed
                    var altUrl = item.DownloadUrl.Replace("/master/", "/main/");
                    data = await _gitHubService.DownloadFileAsync(altUrl);
                }

                if (data != null)
                {
                    var savePath = Path.Combine(CoverFolderPath, item.TargetFilename);
                    await File.WriteAllBytesAsync(savePath, data, token);

                    // Verify file was written
                    if (!File.Exists(savePath))
                    {
                        throw new IOException($"File was not created at {savePath}");
                    }

                    successCount++;
                }
                else
                {
                    Log($"Failed to download {item.GameName}");
                }

                // Check for disk space (rough estimate)
                var driveInfo = new DriveInfo(Path.GetPathRoot(CoverFolderPath) ?? throw new InvalidOperationException("Could not get root path of Cover folder"));
                if (driveInfo.AvailableFreeSpace < 10 * 1024 * 1024) // 10MB threshold
                {
                    throw new IOException("Low disk space detected. Download aborted.");
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
            throw;
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
        // Check if there's an active token source
        if (_cts == null)
        {
            Log("No active operation to cancel.");
            return;
        }

        try
        {
            // Attempt to cancel, handling the case where it's already disposed
            _cts.Cancel();
            Log("Cancellation requested...");
        }
        catch (ObjectDisposedException)
        {
            // Operation already completed and disposed the token source
            Log("Cancellation requested but operation already completed.");
        }
        catch (Exception ex)
        {
            Log($"[CancelOperation] Error cancelling operation: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[CancelOperation] Exception while cancelling operation.");
        }

        // Always dispose and null out the reference
        try
        {
            _cts?.Dispose();
        }
        catch (Exception ex)
        {
            Log($"[CancelOperation] Error disposing cancellation token: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "[CancelOperation] Exception while disposing cancellation token source.");
        }
        finally
        {
            _cts = null; // Critical: prevent future calls on disposed object
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _gitHubService?.Dispose();
        GC.SuppressFinalize(this);
    }
}