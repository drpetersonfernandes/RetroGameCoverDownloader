using RetroGameCoverDownloader.Commands;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Commands;

public class RelayCommandTests
{
    [Fact]
    public void RelayCommandExecuteCallsAction()
    {
        var executed = false;
        var command = new RelayCommand(_ => { executed = true; });

        command.Execute(null);

        Assert.True(executed);
    }

    [Fact]
    public void RelayCommandCanExecuteWithoutPredicateReturnsTrue()
    {
        var command = new RelayCommand(static _ => { });

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void RelayCommandCanExecuteWithPredicateReturnsPredicateResult()
    {
        var command = new RelayCommand(static _ => { }, static _ => false);

        Assert.False(command.CanExecute(null));
    }

    [Fact]
    public void RelayCommandExecutePassesParameter()
    {
        object? received = null;
        var command = new RelayCommand(param => { received = param; });

        command.Execute("test");

        Assert.Equal("test", received);
    }
}

public class AsyncRelayCommandTests
{
    [Fact]
    public async Task AsyncRelayCommandExecuteCallsAsyncAction()
    {
        var executed = false;
        var command = new AsyncRelayCommand(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        command.Execute(null);

        // Allow the async void Execute to run
        await Task.Delay(50);

        Assert.True(executed);
    }

    [Fact]
    public void AsyncRelayCommandCanExecuteWithoutPredicateReturnsTrue()
    {
        var command = new AsyncRelayCommand(static _ => Task.CompletedTask);

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void AsyncRelayCommandCanExecuteWithPredicateReturnsPredicateResult()
    {
        var command = new AsyncRelayCommand(static _ => Task.CompletedTask, static _ => false);

        Assert.False(command.CanExecute(null));
    }

    [Fact]
    public void AsyncRelayCommandCanExecuteWhileExecutingReturnsFalse()
    {
        var tcs = new TaskCompletionSource();
        var command = new AsyncRelayCommand(_ => tcs.Task);

        // Simulate starting execution without awaiting
        command.Execute(null);

        Assert.True(command.IsExecuting);
        Assert.False(command.CanExecute(null));

        tcs.SetResult();
    }

    [Fact]
    public Task AsyncRelayCommandExecuteSwallowsException()
    {
        var command = new AsyncRelayCommand(static _ => throw new InvalidOperationException("test"));

        var ex = Record.Exception(() => command.Execute(null));
        Assert.Null(ex);

        // Allow the async void Execute to complete
        return Task.Delay(50);
    }

    [Fact]
    public Task AsyncRelayCommandExecuteSwallowsAsyncException()
    {
        var command = new AsyncRelayCommand(static async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("test");
        });

        var ex = Record.Exception(() => command.Execute(null));
        Assert.Null(ex);

        // Allow the async void Execute to complete
        return Task.Delay(50);
    }
}