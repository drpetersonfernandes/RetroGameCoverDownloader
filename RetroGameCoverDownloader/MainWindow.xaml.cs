using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using RetroGameCoverDownloader.Managers;
using RetroGameCoverDownloader.Services;
using RetroGameCoverDownloader.Views;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace RetroGameCoverDownloader;

public partial class MainWindow
{
    private readonly ViewModels.MainViewModel _viewModel;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new ViewModels.MainViewModel();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

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
                            _viewModel.Log("[MainWindow.GitHubTokenMenuItem_Click] Token unchanged.");
                            return;
                        }

                        var settings = SettingsManager.LoadSettings();
                        settings.GitHubToken = dialog.Token;
                        SettingsManager.SaveSettings(settings);

                        // Log successful token save (without exposing token) and update the running service
                        _viewModel.UpdateToken(dialog.Token);
                        _viewModel.Log("[MainWindow.OnLoaded] GitHub token saved successfully.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to save token. The application may have limited functionality.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _ = BugReportService.LogErrorAsync(ex, "Failed to save token after dialog.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Show user-friendly error but don't crash
            MessageBox.Show("An error occurred during startup. Some features may not work correctly.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            _ = BugReportService.LogErrorAsync(ex, "Exception during token check on window load.");
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

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosing) return;

        _isClosing = true;

        try
        {
            _viewModel.CancelAll();
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "Error during cancel.");
        }

        try
        {
            _viewModel.Dispose();
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "Error during cleanup.");
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
                    _ = BugReportService.LogErrorAsync(ex, "Failed to save proxy settings.");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("An error occurred while opening proxy settings.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            _ = BugReportService.LogErrorAsync(ex, "Exception opening proxy settings dialog.");
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
                    viewModel?.Log("[MainWindow.GitHubTokenMenuItem_Click] GitHub token saved successfully.");
                    MessageBox.Show("GitHub token saved successfully.",
                        "GitHub Token", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to save token. Please try again.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    _ = BugReportService.LogErrorAsync(ex, "Failed to save token.");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("An error occurred while opening the token dialog.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            _ = BugReportService.LogErrorAsync(ex, "Exception opening token dialog.");
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

                    viewModel?.Log($"[MainWindow.FileExtensionsMenuItem_Click] File extensions updated. {dialog.FileExtensions.Count} extension(s) configured.");
                    MessageBox.Show($"File extensions saved. {dialog.FileExtensions.Count} extension(s) configured.",
                        "File Extensions", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to save file extensions. Please try again.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    _ = BugReportService.LogErrorAsync(ex, "Failed to save file extensions.");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("An error occurred while opening file extensions settings.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            _ = BugReportService.LogErrorAsync(ex, "Exception opening extensions dialog.");
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
            // FIX: Call Log on the ViewModel, not on MainWindow
            var viewModel = DataContext as ViewModels.MainViewModel;
            viewModel?.Log($"Error opening About window: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Error opening About window");
        }
    }

    private async void CheckForUpdatesMenuItem_ClickAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is ViewModels.MainViewModel viewModel)
            {
                viewModel.Log("Checking for updates...");
                await UpdateCheckerService.CheckForUpdateAsync(viewModel.Log);
            }
        }
        catch (Exception ex)
        {
            var viewModel = DataContext as ViewModels.MainViewModel;
            viewModel?.Log($"Error checking for updates: {ex.Message}");
            _ = BugReportService.LogErrorAsync(ex, "Error checking for updates from menu.");
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
            _ = BugReportService.LogErrorAsync(ex, $"Error opening update URL: {e.Uri.AbsoluteUri}");
        }

        e.Handled = true;
    }
}