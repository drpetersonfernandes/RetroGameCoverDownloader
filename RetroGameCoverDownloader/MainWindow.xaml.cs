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
    public MainWindow()
    {
        InitializeComponent();
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
                var dialog = new TokenDialog();
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        settings.GitHubToken = dialog.Token;
                        SettingsManager.SaveSettings(settings);

                        // Log successful token save (without exposing token)
                        var viewModel = DataContext as ViewModels.MainViewModel;
                        viewModel?.Log("[OnLoaded] GitHub token saved successfully.");
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
        Close();
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