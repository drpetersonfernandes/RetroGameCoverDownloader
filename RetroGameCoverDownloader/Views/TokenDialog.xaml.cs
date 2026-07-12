using System.Windows;
using System.Windows.Navigation;
using RetroGameCoverDownloader.Services;
using Serilog;
using MessageBox = System.Windows.MessageBox;

namespace RetroGameCoverDownloader.Views;

public partial class TokenDialog
{
    public string Token { get; private set; } = "";

    public bool HasExistingToken { get; }

    public TokenDialog(bool hasExistingToken = false)
    {
        HasExistingToken = hasExistingToken;
        InitializeComponent();

        if (HasExistingToken)
        {
            ExistingTokenPanel.Visibility = Visibility.Visible;
            TokenBox.Focus();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var entered = TokenBox.Password.Trim();
            if (string.IsNullOrEmpty(entered) && HasExistingToken)
            {
                Token = "";
                DialogResult = true;
                return;
            }

            Token = entered;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save token.");
            MessageBox.Show("An error occurred while saving the token. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        UpdateCheckerService.OpenUrlInBrowser(e.Uri.AbsoluteUri);
    }
}
