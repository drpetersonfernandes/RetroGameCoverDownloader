using System.Threading.Tasks;
using RetroGameCoverDownloader.Commands;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Commands;

public class RelayCommandTests
{
    [Fact]
    public void RelayCommand_Execute_CallsAction()
    {
        var executed = false;
        var command = new RelayCommand(_ => executed = true);

        command.Execute(null);

        Assert.True(executed);
    }

    [Fact]
    public void RelayCommand_CanExecute_WithoutPredicate_ReturnsTrue()
    {
        var command = new RelayCommand(_ => { });

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void RelayCommand_CanExecute_WithPredicate_ReturnsPredicateResult()
    {
        var command = new RelayCommand(_ => { }, _ => false);

        Assert.False(command.CanExecute(null));
    }

    [Fact]
    public void RelayCommand_Execute_PassesParameter()
    {
        object? received = null;
        var command = new RelayCommand(param => received = param);

        command.Execute("test");

        Assert.Equal("test", received);
    }
}

public class AsyncRelayCommandTests
{
    [Fact]
    public async Task AsyncRelayCommand_Execute_CallsAsyncAction()
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
    public void AsyncRelayCommand_CanExecute_WithoutPredicate_ReturnsTrue()
    {
        var command = new AsyncRelayCommand(_ => Task.CompletedTask);

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void AsyncRelayCommand_CanExecute_WithPredicate_ReturnsPredicateResult()
    {
        var command = new AsyncRelayCommand(_ => Task.CompletedTask, _ => false);

        Assert.False(command.CanExecute(null));
    }

    [Fact]
    public void AsyncRelayCommand_CanExecute_WhileExecuting_ReturnsFalse()
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
    public async Task AsyncRelayCommand_Execute_SwallowsException()
    {
        var command = new AsyncRelayCommand(_ => throw new InvalidOperationException("test"));

        var ex = Record.Exception(() => command.Execute(null));
        Assert.Null(ex);

        // Allow the async void Execute to complete
        await Task.Delay(50);
    }

    [Fact]
    public async Task AsyncRelayCommand_Execute_SwallowsAsyncException()
    {
        var command = new AsyncRelayCommand(async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("test");
        });

        var ex = Record.Exception(() => command.Execute(null));
        Assert.Null(ex);

        // Allow the async void Execute to complete
        await Task.Delay(50);
    }
}
