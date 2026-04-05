using System.Windows;
using System.Windows.Threading;
using RetroGameCoverDownloader.Services;
using RetroGameCoverDownloader.ViewModels;
using MessageBox = System.Windows.MessageBox;

namespace RetroGameCoverDownloader;

/// <inheritdoc />
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        // Parse command-line arguments
        // Usage:
        //   RetroGameCoverDownloader.exe "C:\ROMs" "C:\Covers"
        //   RetroGameCoverDownloader.exe --rom "C:\ROMs" --cover "C:\Covers"
        //   RetroGameCoverDownloader.exe /rom "C:\ROMs" /cover "C:\Covers"
        string? romPath = null;
        string? coverPath = null;

        var args = e.Args;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            // Support both / and -- prefixes
            if (arg.Equals("/rom", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--rom", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    romPath = args[++i];
                }
            }
            else if (arg.Equals("/cover", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("--cover", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    coverPath = args[++i];
                }
            }
        }

        // Fallback to positional arguments if no flags provided
        if (romPath == null && coverPath == null && args.Length >= 2)
        {
            romPath = args[0];
            coverPath = args[1];
        }

        // Create and show the main window
        var mainWindow = new MainWindow();

        // Set paths in ViewModel if provided
        if (mainWindow.DataContext is MainViewModel viewModel)
        {
            if (!string.IsNullOrWhiteSpace(romPath))
            {
                viewModel.RomFolderPath = romPath;
            }

            if (!string.IsNullOrWhiteSpace(coverPath))
            {
                viewModel.CoverFolderPath = coverPath;
            }
        }

        mainWindow.Show();
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            // Log the exception
            BugReportService.LogErrorSync(e.Exception, "An unhandled exception occurred.");
        }
        catch (Exception logEx)
        {
            // If bug reporting fails, at least try to log to a critical file
            try
            {
                var criticalMsg = $"[{DateTime.Now}] CRITICAL: BugReportService failed during unhandled exception. Original: {e.Exception?.Message}. Logging error: {logEx.Message}";
                System.IO.File.AppendAllText("critical_startup_error.log", criticalMsg);
            }
            catch
            {
                /* Last resort - ignore */
            }
        }

        // Notify the user
        MessageBox.Show(
            "An unexpected error occurred. The application will now close. A bug report has been saved to error.log and sent to the developer.",
            "Unhandled Exception",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Prevent default unhandled exception processing and shut down
        e.Handled = true;
        Current.Shutdown();
    }
}