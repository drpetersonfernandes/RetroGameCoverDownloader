using System.Windows;
using RetroGameCoverDownloader.Managers;
using Serilog;
using MessageBox = System.Windows.MessageBox;

namespace RetroGameCoverDownloader.Views;

public partial class ExtensionsDialog
{
    public List<string> FileExtensions { get; private set; } = [];

    public ExtensionsDialog()
    {
        InitializeComponent();
        LoadCurrentExtensions();
    }

    private void LoadCurrentExtensions()
    {
        try
        {
            var settings = SettingsManager.LoadSettings();
            FileExtensions = settings.FileExtensions.ToList();

            ExtensionsListBox.Items.Clear();
            foreach (var ext in FileExtensions.OrderBy(static e => e.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase))
            {
                ExtensionsListBox.Items.Add(ext);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load current extensions.");
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var input = NewExtensionBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(input))
        {
            MessageBox.Show("Please enter a file extension.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            NewExtensionBox.Focus();
            return;
        }

        if (!input.StartsWith('.'))
        {
            input = "." + input;
        }

        input = input.ToLowerInvariant();

        if (ExtensionsListBox.Items.Cast<string>().Any(ext =>
                string.Equals(ext, input, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("This extension is already in the list.", "Duplicate",
                MessageBoxButton.OK, MessageBoxImage.Information);
            NewExtensionBox.Focus();
            return;
        }

        ExtensionsListBox.Items.Add(input);
        NewExtensionBox.Clear();
        NewExtensionBox.Focus();
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExtensionsListBox.SelectedItem is string selected)
        {
            ExtensionsListBox.Items.Remove(selected);
        }
        else
        {
            MessageBox.Show("Please select an extension to remove.", "No Selection",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsListBox.Items.Clear();
        foreach (var ext in Models.AppSettings.DefaultExtensions.OrderBy(static x => x.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase))
        {
            ExtensionsListBox.Items.Add(ext);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FileExtensions = ExtensionsListBox.Items.Cast<string>().ToList();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save extensions.");
            MessageBox.Show("An error occurred while saving. Please try again.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
