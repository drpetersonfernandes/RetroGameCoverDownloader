using System.Globalization;
using System.Windows.Data;
using RetroGameCoverDownloader.Converters;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Converters;

public class InverseBoolConverterTests
{
    private readonly InverseBoolConverter _converter = new();

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Convert_BoolValues_ReturnsInverse(bool input, bool expected)
    {
        var result = _converter.Convert(input, typeof(bool), null!, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ConvertBack_BoolValues_ReturnsInverse(bool input, bool expected)
    {
        var result = _converter.ConvertBack(input, typeof(bool), null!, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_NonBoolValue_ReturnsOriginalValue()
    {
        var result = _converter.Convert("hello", typeof(bool), null!, CultureInfo.InvariantCulture);

        Assert.Equal("hello", result);
    }

    [Fact]
    public void ConvertBack_NonBoolValue_ReturnsOriginalValue()
    {
        var result = _converter.ConvertBack(123, typeof(bool), null!, CultureInfo.InvariantCulture);

        Assert.Equal(123, result);
    }
}
