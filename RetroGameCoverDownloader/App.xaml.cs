using System.Windows;
using System.Windows.Threading;
using RetroGameCoverDownloader.Services;
using MessageBox = System.Windows.MessageBox;

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
        // Log the exception
        BugReportService.LogErrorSync(e.Exception, "An unhandled exception occurred.");

        // Notify the user
        MessageBox.Show("An unexpected error occurred. The application will now close. A bug report has been saved to error.log and sent to the developer.", "Unhandled Exception", MessageBoxButton.OK, MessageBoxImage.Error);

        // Prevent default unhandled exception processing and shut down
        e.Handled = true;
        Current.Shutdown();
    }
}