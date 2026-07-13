using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Navigation;
using RetroGameCoverDownloader.Helpers;
using RetroGameCoverDownloader.Managers;
using RetroGameCoverDownloader.Services;
using RetroGameCoverDownloader.Views;
using Serilog;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace RetroGameCoverDownloader;

public partial class MainWindow
{
    private const int WmHotkey = 0x0312;
    private const int ModNone = 0x0000;
    private const int HotkeyId = 9001;

    private readonly ViewModels.MainViewModel _viewModel;
    private HwndSource? _hwndSource;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new ViewModels.MainViewModel();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        _hwndSource?.AddHook(WndProc);

        var handle = _hwndSource?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
            return;

        var f8KeyCode = KeyInterop.VirtualKeyFromKey(Key.F8);
        var registered = RegisterHotKey(handle, HotkeyId, ModNone, (uint)f8KeyCode);

        if (!registered)
        {
            Log.Information("[MainWindow] Failed to register F8 global hotkey. It may be in use by another application.");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            HandleScreenshotHotkey();
        }

        return IntPtr.Zero;
    }

    private void HandleScreenshotHotkey()
    {
        var (success, filePath) = ScreenshotService.CaptureForegroundWindow();

        if (success && filePath != null)
        {
            Log.Information("[MainWindow] Screenshot saved: {FilePath}", filePath);
        }
        else
        {
            Dispatcher.Invoke(() =>
            {
                Log.Information("[MainWindow] Failed to save screenshot.");
                MessageBox.Show(
                    "Could not save the screenshot. The application folder may not have write permissions.\n\n" +
                    "Try running the application as Administrator or from a location with write access.",
                    "Screenshot Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_viewModel.HasGitHubToken)
            {
                var dialog = new TokenDialog { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(dialog.Token) && _viewModel.HasGitHubToken)
                        {
                            Log.Information("[MainWindow.GitHubTokenMenuItem_Click] Token unchanged.");
                            return;
                        }

                        var settings = SettingsManager.LoadSettings();
                        settings.GitHubToken = dialog.Token;
                        SettingsManager.SaveSettings(settings);

                        // Log successful token save (without exposing token) and update the running service
                        _viewModel.UpdateToken(dialog.Token);
                        Log.Information("[MainWindow.OnLoaded] GitHub token saved successfully.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to save token. The application may have limited functionality.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Log.Error(ex, "Failed to save token after dialog.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Show user-friendly error but don't crash
            MessageBox.Show("An error occurred during startup. Some features may not work correctly.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            Log.Error(ex, "Exception during token check on window load.");
        }
    }

    // Auto-scroll the log
    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ((TextBox)sender).ScrollToEnd();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenAppDataPathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = AppInfo.LocalAppDataFolderPath;
            Directory.CreateDirectory(path);

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open AppData folder.");
            MessageBox.Show("Could not open the application data folder.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosing) return;

        _isClosing = true;

        try
        {
            if (_hwndSource?.Handle is { } handle && handle != IntPtr.Zero)
            {
                UnregisterHotKey(handle, HotkeyId);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while unregistering hotkey.");
        }

        try
        {
            _viewModel.CancelAll();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during cancel.");
        }

        try
        {
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error disposing HwndSource.");
        }

        try
        {
            _viewModel.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during cleanup.");
        }
    }

    private void ProxySettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ProxySettingsDialog { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Load current settings
                    var settings = SettingsManager.LoadSettings();

                    // Update proxy settings
                    settings.UseProxy = dialog.UseProxy;
                    settings.ProxyHost = dialog.ProxyHost;
                    settings.ProxyPort = dialog.ProxyPort;
                    settings.ProxyUsername = dialog.ProxyUsername;
                    settings.ProxyPassword = dialog.ProxyPassword;

                    SettingsManager.SaveSettings(settings);

                    // Update the running service with new proxy settings
                    var viewModel = DataContext as ViewModels.MainViewModel;
                    viewModel?.UpdateProxySettings(
                        dialog.UseProxy,
                        dialog.ProxyHost,
                        dialog.ProxyPort,
                        dialog.ProxyUsername,
                        dialog.ProxyPassword);

                    var proxyStatus = Models.AppSettings.FormatProxyStatus(dialog.UseProxy, dialog.ProxyHost, dialog.ProxyPort);
                    MessageBox.Show($"Proxy settings saved and applied. Proxy: {proxyStatus}",
                        "Proxy Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to save proxy settings. Please try again.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Log.Error(ex, "Failed to save proxy settings.");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("An error occurred while opening proxy settings.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error(ex, "Exception opening proxy settings dialog.");
        }
    }

    private void GitHubTokenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new TokenDialog(_viewModel.HasGitHubToken) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var settings = SettingsManager.LoadSettings();
                    settings.GitHubToken = dialog.Token;
                    SettingsManager.SaveSettings(settings);

                    var viewModel = DataContext as ViewModels.MainViewModel;
                    viewModel?.UpdateToken(dialog.Token);
                    Log.Information("[MainWindow.GitHubTokenMenuItem_Click] GitHub token saved successfully.");
                    MessageBox.Show("GitHub token saved successfully.",
                        "GitHub Token", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to save token. Please try again.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Log.Error(ex, "Failed to save token.");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("An error occurred while opening the token dialog.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error(ex, "Exception opening token dialog.");
        }
    }

    private void FileExtensionsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ExtensionsDialog { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var settings = SettingsManager.LoadSettings();
                    settings.FileExtensions = dialog.FileExtensions;
                    SettingsManager.SaveSettings(settings);

                    var viewModel = DataContext as ViewModels.MainViewModel;
                    viewModel?.UpdateFileExtensions(dialog.FileExtensions);

                    Log.Information("[MainWindow.FileExtensionsMenuItem_Click] File extensions updated. {ExtensionCount} extension(s) configured.", dialog.FileExtensions.Count);
                    MessageBox.Show($"File extensions saved. {dialog.FileExtensions.Count} extension(s) configured.",
                        "File Extensions", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to save file extensions. Please try again.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Log.Error(ex, "Failed to save file extensions.");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("An error occurred while opening file extensions settings.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error(ex, "Exception opening extensions dialog.");
        }
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error opening About window: {Reason}", ex.Message);
        }
    }

    private async void CheckForUpdatesMenuItem_ClickAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is ViewModels.MainViewModel)
            {
                Log.Information("Checking for updates...");
                await UpdateCheckerService.CheckForUpdateAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Information("Error checking for updates: {Reason}", ex.Message);
            Log.Error(ex, "Error checking for updates from menu.");
        }
    }

    private void UpdateHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            UpdateCheckerService.OpenUrlInBrowser(e.Uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error opening update URL: {e.Uri.AbsoluteUri}");
        }

        e.Handled = true;
    }
}