using System.Windows;
using System.Windows.Controls;
using RetroGameCoverDownloader.Managers;
using RetroGameCoverDownloader.Services;
using RetroGameCoverDownloader.Views;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace RetroGameCoverDownloader;

public partial class MainWindow
{
    private readonly ViewModels.MainViewModel _viewModel;

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
            // Check for Token on startup
            var settings = SettingsManager.LoadSettings();

            if (string.IsNullOrWhiteSpace(settings.GitHubToken))
            {
                var dialog = new TokenDialog { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                try
                {
                    settings.GitHubToken = dialog.Token;
                    SettingsManager.SaveSettings(settings);

                    // Log successful token save (without exposing token) and update the running service
                    _viewModel.UpdateToken(dialog.Token);
                    _viewModel.Log("[OnLoaded] GitHub token saved successfully.");
                }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to save token. The application may have limited functionality.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _ = BugReportService.LogErrorAsync(ex, "[OnLoaded] Failed to save token after dialog.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Show user-friendly error but don't crash
            MessageBox.Show("An error occurred during startup. Some features may not work correctly.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            _ = BugReportService.LogErrorAsync(ex, "[OnLoaded] Exception during token check on window load.");
        }
    }

    // Auto-scroll the log
    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ((TextBox)sender).ScrollToEnd();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.CancelAll();
            _viewModel.Dispose();
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "[ExitMenuItem_Click] Error during cleanup.");
        }

        Close();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            _viewModel.CancelAll();
            _viewModel.Dispose();
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "[MainWindow_Closing] Error during cleanup.");
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

                    var proxyStatus = dialog.UseProxy ? $"enabled (http://{dialog.ProxyHost}:{dialog.ProxyPort})" : "disabled";
                    MessageBox.Show($"Proxy settings saved and applied. Proxy: {proxyStatus}",
                        "Proxy Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to save proxy settings. Please try again.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    _ = BugReportService.LogErrorAsync(ex, "[ProxySettingsMenuItem_Click] Failed to save proxy settings.");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("An error occurred while opening proxy settings.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            _ = BugReportService.LogErrorAsync(ex, "[ProxySettingsMenuItem_Click] Exception opening proxy settings dialog.");
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
}