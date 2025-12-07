using System.Windows;
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

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Token = TokenBox.Text.Trim();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            // Log and show user-friendly error
            _ = BugReportService.LogErrorAsync(ex, "[TokenDialog.Button_Click] Failed to save token.");
            MessageBox.Show("An error occurred while saving the token. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}