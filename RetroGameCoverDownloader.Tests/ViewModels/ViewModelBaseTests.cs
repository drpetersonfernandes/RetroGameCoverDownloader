using RetroGameCoverDownloader.ViewModels;
using Xunit;

namespace RetroGameCoverDownloader.Tests.ViewModels;

public class ViewModelBaseTests
{
    #region SetField

    [Fact]
    public void SetFieldWithDifferentValueUpdatesFieldAndReturnsTrue()
    {
        var vm = new TestViewModel();

        var changed = vm.SetName("Hello");

        Assert.True(changed);
        Assert.Equal("Hello", vm.Name);
    }

    [Fact]
    public void SetFieldWithDifferentValueRaisesPropertyChanged()
    {
        var vm = new TestViewModel();
        string? raisedProperty = null;
        vm.PropertyChanged += (_, e) => { raisedProperty = e.PropertyName; };

        vm.SetName("World");

        Assert.Equal(nameof(TestViewModel.Name), raisedProperty);
    }

    [Fact]
    public void SetFieldWithEqualValueReturnsFalse()
    {
        var vm = new TestViewModel();
        vm.SetName("Same");

        var changed = vm.SetName("Same");

        Assert.False(changed);
    }

    [Fact]
    public void SetFieldWithEqualValueDoesNotRaisePropertyChanged()
    {
        var vm = new TestViewModel();
        vm.SetName("Same");

        var raiseCount = 0;
        vm.PropertyChanged += (_, _) => { raiseCount++; };

        vm.SetName("Same");

        Assert.Equal(0, raiseCount);
    }

    [Fact]
    public void SetFieldWithIntValueUpdatesAndRaises()
    {
        var vm = new TestViewModel();
        string? raisedProperty = null;
        vm.PropertyChanged += (_, e) => { raisedProperty = e.PropertyName; };

        var changed = vm.SetCount(42);

        Assert.True(changed);
        Assert.Equal(42, vm.Count);
        Assert.Equal(nameof(TestViewModel.Count), raisedProperty);
    }

    [Fact]
    public void SetFieldFromNullToValueReturnsTrue()
    {
        var vm = new TestViewModel();

        var changed = vm.SetName("NotNull");

        Assert.True(changed);
        Assert.Equal("NotNull", vm.Name);
    }

    [Fact]
    public void SetFieldFromValueToNullReturnsTrue()
    {
        var vm = new TestViewModel();
        vm.SetName("Value");

        var changed = vm.SetName(null);

        Assert.True(changed);
        Assert.Null(vm.Name);
    }

    [Fact]
    public void SetFieldWithBothNullReturnsFalse()
    {
        var vm = new TestViewModel();

        var changed = vm.SetName(null);

        Assert.False(changed);
    }

    #endregion

    #region OnPropertyChanged

    [Fact]
    public void OnPropertyChangedRaisesWithGivenName()
    {
        var vm = new TestViewModel();
        string? raisedProperty = null;
        vm.PropertyChanged += (_, e) => { raisedProperty = e.PropertyName; };

        vm.RaiseCustom("CustomProperty");

        Assert.Equal("CustomProperty", raisedProperty);
    }

    [Fact]
    public void OnPropertyChangedWithNoSubscribersDoesNotThrow()
    {
        var vm = new TestViewModel();

        var exception = Record.Exception(() => vm.RaiseCustom("Anything"));

        Assert.Null(exception);
    }

    #endregion

    private sealed class TestViewModel : ViewModelBase
    {
        private string? _name;
        private int _count;

        public string? Name => _name;
        public int Count => _count;

        public bool SetName(string? value)
        {
            return SetField(ref _name, value, nameof(Name));
        }

        public bool SetCount(int value)
        {
            return SetField(ref _count, value, nameof(Count));
        }

        public void RaiseCustom(string propertyName)
        {
            OnPropertyChanged(propertyName);
        }
    }
}
