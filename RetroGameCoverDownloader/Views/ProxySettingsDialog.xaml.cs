using System.Globalization;
using System.Windows;
using RetroGameCoverDownloader.Managers;
using RetroGameCoverDownloader.Services;
using MessageBox = System.Windows.MessageBox;

namespace RetroGameCoverDownloader.Views;

public partial class ProxySettingsDialog
{
    public bool UseProxy { get; private set; }
    public string? ProxyHost { get; private set; }
    public int ProxyPort { get; private set; }
    public string? ProxyUsername { get; private set; }
    public string? ProxyPassword { get; private set; }

    public ProxySettingsDialog()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        try
        {
            var settings = SettingsManager.LoadSettings();
            UseProxyCheckBox.IsChecked = settings.UseProxy;
            ProxyHostBox.Text = settings.ProxyHost ?? string.Empty;
            ProxyPortBox.Text = settings.ProxyPort > 0 ? settings.ProxyPort.ToString(CultureInfo.InvariantCulture) : string.Empty;
            ProxyUsernameBox.Text = settings.ProxyUsername ?? string.Empty;
            ProxyPasswordBox.Password = settings.ProxyPassword ?? string.Empty;

            // Update panel enabled state
            ProxySettingsPanel.IsEnabled = settings.UseProxy;
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "[ProxySettingsDialog] Failed to load current settings.");
        }
    }

    private void UseProxyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ProxySettingsPanel.IsEnabled = UseProxyCheckBox.IsChecked == true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Validate inputs if proxy is enabled
            if (UseProxyCheckBox.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(ProxyHostBox.Text))
                {
                    MessageBox.Show("Please enter a proxy host address.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ProxyHostBox.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(ProxyPortBox.Text))
                {
                    MessageBox.Show("Please enter a proxy port.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ProxyPortBox.Focus();
                    return;
                }

                if (!int.TryParse(ProxyPortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port <= 0 || port > 65535)
                {
                    MessageBox.Show("Please enter a valid proxy port (1-65535).", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ProxyPortBox.Focus();
                    return;
                }
            }

            // Set properties
            UseProxy = UseProxyCheckBox.IsChecked == true;
            ProxyHost = UseProxy ? ProxyHostBox.Text.Trim() : null;
            ProxyPort = UseProxy && int.TryParse(ProxyPortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0;
            ProxyUsername = UseProxy && !string.IsNullOrWhiteSpace(ProxyUsernameBox.Text)
                ? ProxyUsernameBox.Text.Trim()
                : null;
            ProxyPassword = UseProxy && !string.IsNullOrWhiteSpace(ProxyPasswordBox.Password)
                ? ProxyPasswordBox.Password
                : null;

            DialogResult = true;
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "[ProxySettingsDialog.SaveButton_Click] Failed to save proxy settings.");
            MessageBox.Show("An error occurred while saving the proxy settings. Please try again.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
