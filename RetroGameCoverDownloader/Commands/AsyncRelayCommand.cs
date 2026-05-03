using System.Windows.Input;
using RetroGameCoverDownloader.Services;

namespace RetroGameCoverDownloader.Commands;

public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (_isExecuting == value) return;

            _isExecuting = value;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool CanExecute(object? parameter)
    {
        return !IsExecuting && (_canExecute == null || _canExecute(parameter));
    }

    public async void Execute(object? parameter)
    {
        try
        {
            if (!CanExecute(parameter)) return;

            IsExecuting = true;
            try
            {
                await _execute(parameter);
            }
            catch
            {
                // Prevent exceptions from reaching the WPF synchronization context.
                // ViewModel methods are expected to handle their own logging.
            }
            finally
            {
                IsExecuting = false;
            }
        }
        catch (Exception ex)
        {
            _ = BugReportService.LogErrorAsync(ex, "[AsyncRelayCommand] Unhandled exception in Execute.");
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
