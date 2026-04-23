using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using RetroGameCoverDownloader.Helpers;
using RetroGameCoverDownloader.Services;
using MessageBox = System.Windows.MessageBox;

namespace RetroGameCoverDownloader.Views;

public partial class AboutWindow
{
    public AboutWindow()
    {
        InitializeComponent();

        AppVersionTextBlock.Text = $"Version: {AppInfo.VersionString}";
        DescriptionTextBlock.Text = "A utility for downloading game cover art from libretro-thumbnails GitHub repositories for your ROM collection.";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            // Notify developer
            _ = BugReportService.LogErrorAsync(ex, $"Error opening URL: {e.Uri.AbsoluteUri}");

            // Notify user
            MessageBox.Show($"Unable to open link: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // Mark the event as handled
        e.Handled = true;
    }

}