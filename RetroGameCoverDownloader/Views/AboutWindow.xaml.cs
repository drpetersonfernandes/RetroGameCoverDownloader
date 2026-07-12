using System.Windows;
using System.Windows.Navigation;
using RetroGameCoverDownloader.Helpers;
using RetroGameCoverDownloader.Services;

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
        e.Handled = true;
        UpdateCheckerService.OpenUrlInBrowser(e.Uri.AbsoluteUri);
    }
}