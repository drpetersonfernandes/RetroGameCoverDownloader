using System.Windows;

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
        Token = TokenBox.Text.Trim();
        DialogResult = true;
    }
}