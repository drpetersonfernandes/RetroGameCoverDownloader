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

