using System.Windows;
using System.Windows.Controls;
using RetroGameCoverDownloader.Managers;
using RetroGameCoverDownloader.Views;
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
        // Check for Token on startup
        var settings = SettingsManager.LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.GitHubToken))
        {
            var dialog = new TokenDialog();
            if (dialog.ShowDialog() == true)
            {
                settings.GitHubToken = dialog.Token;
                SettingsManager.SaveSettings(settings);

                // Refresh ViewModel with new token if needed,
                // or simply restart app logic.
                // For simplicity, the VM loads settings in constructor,
                // so we might need to reload the VM or pass the token.
                // Ideally, do this check in App.xaml.cs before showing MainWindow.
            }
        }
    }

    // Auto-scroll the log
    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ((TextBox)sender).ScrollToEnd();
    }
}