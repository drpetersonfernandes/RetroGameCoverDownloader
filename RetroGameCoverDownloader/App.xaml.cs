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
    private static int _shutdownGuard;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Subscribe to all unhandled exception handlers to ensure every bug is forwarded
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        // Fire-and-forget launch telemetry
        _ = ApplicationStatsService.TrackLaunchAsync();

        try
        {
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
        catch (Exception ex)
        {
            try
            {
                BugReportService.LogErrorSync(ex, "Unhandled exception during application startup.");
            }
            catch (Exception logEx)
            {
                try
                {
                    var criticalMsg = $"[{DateTime.Now}] CRITICAL: BugReportService failed during startup. Original: {ex.Message}. Logging error: {logEx.Message}";
                    System.IO.File.AppendAllText("critical_startup_error.log", criticalMsg);
                }
                catch
                {
                    /* Last resort - ignore */
                }
            }

            MessageBox.Show(
                "An unexpected error occurred during startup. The application will now close.",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Current.Shutdown();
            Environment.Exit(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        Environment.Exit(0);
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        if (Interlocked.Exchange(ref _shutdownGuard, 1) != 0)
        {
            return;
        }

        try
        {
            BugReportService.LogErrorSync(e.Exception, "An unhandled dispatcher exception occurred.");
        }
        catch (Exception logEx)
        {
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

        try
        {
            MessageBox.Show(
                "An unexpected error occurred. The application will now close. A bug report has been saved to error.log and sent to the developer.",
                "Unhandled Exception",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            /* Ignore message box failure */
        }

        Current.Shutdown();
        Environment.Exit(1);
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            var exception = e.ExceptionObject as Exception ?? new InvalidOperationException($"Non-exception object thrown: {e.ExceptionObject}");
            BugReportService.LogErrorSync(exception, "An unhandled AppDomain exception occurred.");
        }
        catch (Exception logEx)
        {
            try
            {
                var criticalMsg = $"[{DateTime.Now}] CRITICAL: BugReportService failed during AppDomain unhandled exception. Logging error: {logEx.Message}";
                System.IO.File.AppendAllText("critical_appdomain_error.log", criticalMsg);
            }
            catch
            {
                /* Last resort - ignore */
            }
        }

        try
        {
            MessageBox.Show(
                "A fatal error occurred on a background thread. The application will now close.",
                "Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            /* Ignore message box failure */
        }

        if (e.IsTerminating)
        {
            Current.Shutdown();
            Environment.Exit(1);
        }
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            BugReportService.LogErrorSync(e.Exception, "An unobserved task exception occurred.");
        }
        catch (Exception logEx)
        {
            try
            {
                var criticalMsg = $"[{DateTime.Now}] CRITICAL: BugReportService failed during unobserved task exception. Logging error: {logEx.Message}";
                System.IO.File.AppendAllText("critical_task_error.log", criticalMsg);
            }
            catch
            {
                /* Last resort - ignore */
            }
        }

        // Mark the exception as observed to prevent process termination
        e.SetObserved();
    }
}
