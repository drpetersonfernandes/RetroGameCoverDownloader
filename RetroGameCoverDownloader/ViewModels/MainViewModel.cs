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

    public MainViewModel()
    {
        // Load Settings
        var settings = SettingsManager.LoadSettings();

        // Token Check Logic
        if (string.IsNullOrWhiteSpace(settings.GitHubToken))
        {
            // In a real app, you might open a Dialog Window here.
            // For simplicity, we assume the View handles the initial prompt or we just init service without token.
        }

        _gitHubService = new GitHubService(settings.GitHubToken);

        // 2. Subscribe to the Rate Limit event
        _gitHubService.RateLimitHit += OnRateLimitHit;

        // 3. Initialize the timer
        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += OnTimerTick;

        // Init Commands
        BrowseRomCommand = new RelayCommand(_ => SelectFolder(path => { RomFolderPath = path; }));
        BrowseCoverCommand = new RelayCommand(_ => SelectFolder(path => { CoverFolderPath = path; }));
        PrepareCommand = new RelayCommand(async void (_) =>
        {
            try
            {
                await PrepareDownloadAsync();
            }
            catch (Exception ex)
            {
                Log($"[PrepareCommand] Error: {ex.Message}].");
            }
        }, _ => !IsBusy && SelectedSystem != null && !string.IsNullOrEmpty(RomFolderPath) && !string.IsNullOrEmpty(CoverFolderPath));
        DownloadCommand = new RelayCommand(async void (_) =>
        {
            try
            {
                await DownloadCoversAsync();
            }
            catch (Exception ex)
            {
                Log($"[DownloadCommand] Error: {ex.Message}].");
            }
        }, _ => !IsBusy && _itemsToDownload.Count > 0);
        CancelCommand = new RelayCommand(_ => CancelOperation(), _ => IsBusy);

        // Load Systems on Startup
        LoadSystemsAsync();
    }

    // 5. Handle the timer tick
    private void OnTimerTick(object? sender, EventArgs e)
    {
        _remainingWaitTime = _remainingWaitTime.Subtract(TimeSpan.FromSeconds(1));

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
        _remainingWaitTime = waitTime;

        // Ensure we update UI on the UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = $"Rate limit reached. Resuming in {_remainingWaitTime.TotalSeconds:F0} seconds...";
            _countdownTimer.Start();
        });
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
    private void Log(string message)
    {
        LogText += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
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
            var systems = await Task.Run(() => _gitHubService.GetAvailableSystemsAsync(Log));

            Application.Current.Dispatcher.Invoke(() =>
            {
                Systems.Clear();
                foreach (var sys in systems.OrderBy(s => s.SystemName)) Systems.Add(sys);
            });

            Log($"Loaded {systems.Count} systems.");
            IsBusy = false;
        }
        catch (Exception ex)
        {
            Log($"[LoadSystemsAsync] Error: {ex.Message}].");
        }
    }

    private void SelectFolder(Action<string> setPath)
    {
        using var dialog = new FolderBrowserDialog();
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            setPath(dialog.SelectedPath);
        }
    }

    private async Task PrepareDownloadAsync()
    {
        if (SelectedSystem == null) return;

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
            });
        }
        catch (Exception ex)
        {
            Log($"Error during preparation: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            // Force command refresh to enable Download button
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task DownloadCoversAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        ProgressMax = _itemsToDownload.Count;
        ProgressValue = 0;
        var successCount = 0;

        Log("--- Starting Download ---");

        try
        {
            foreach (var item in _itemsToDownload)
            {
                if (token.IsCancellationRequested)
                {
                    Log("Download cancelled by user.");
                    break;
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
                    successCount++;
                }
                else
                {
                    Log($"Failed to download {item.GameName}");
                }

                ProgressValue++;
            }
        }
        catch (Exception ex)
        {
            Log($"Error during download batch: {ex.Message}");
        }
        finally
        {
            Log($"Download finished. Successfully saved {successCount} covers.");
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
            _itemsToDownload.Clear(); // Reset list
        }
    }

    private void CancelOperation()
    {
        _cts?.Cancel();
        Log("Cancellation requested...");
    }

    public void Dispose()
    {
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
