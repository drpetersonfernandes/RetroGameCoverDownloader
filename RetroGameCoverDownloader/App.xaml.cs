using System.Windows;
using System.Windows.Threading;
using RetroGameCoverDownloader.Services;
using MessageBox = System.Windows.MessageBox;

// Added for DateTime

namespace RetroGameCoverDownloader;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
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