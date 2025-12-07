using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using RetroGameCoverDownloader.Services;
using MessageBox = System.Windows.MessageBox;

namespace RetroGameCoverDownloader.Views;

public partial class TokenDialog
{
    public string Token { get; private set; } = "";

    public TokenDialog()
    {
        InitializeComponent();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Token = TokenBox.Text.Trim();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "[TokenDialog.SaveButton_Click] Failed to save token.");
            MessageBox.Show("An error occurred while saving the token. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "[TokenDialog.Hyperlink_RequestNavigate] Failed to open hyperlink.");
            MessageBox.Show($"Could not open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
