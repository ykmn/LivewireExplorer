using System.Windows;
using LivewireBrowser.App;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class ProgressFillConverterTests
{
    // Regression test for the vertical meter bars rendering with no visible color fill:
    // ProgressBar's built-in PART_Track/PART_Indicator auto-sizing (which the default theme
    // relies on) was confirmed, via an isolated repro app, to leave the indicator at 0 height
    // for a custom vertical template. MeterBarStyle in Themes/DarkTheme.xaml replaces that
    // with two Star-weighted grid rows computed by this converter instead.

    [Theory]
    [InlineData(0.6, 0.6)]
    [InlineData(0.0, 0.0001)]
    [InlineData(1.0, 1.0)]
    public void Convert_FillParameter_ReturnsValueAsStarWeight(double value, double expectedStars)
    {
        var converter = new ProgressFillConverter();

        var result = (GridLength)converter.Convert(value, typeof(GridLength), null!, null!);

        Assert.Equal(GridUnitType.Star, result.GridUnitType);
        Assert.Equal(expectedStars, result.Value, precision: 3);
    }

    [Theory]
    [InlineData(0.6, 0.4)]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.0001)]
    public void Convert_EmptyParameter_ReturnsInverseAsStarWeight(double value, double expectedStars)
    {
        var converter = new ProgressFillConverter();

        var result = (GridLength)converter.Convert(value, typeof(GridLength), "Empty", null!);

        Assert.Equal(GridUnitType.Star, result.GridUnitType);
        Assert.Equal(expectedStars, result.Value, precision: 3);
    }

    [Fact]
    public void Convert_OutOfRangeValue_IsClamped()
    {
        var converter = new ProgressFillConverter();

        var tooHigh = (GridLength)converter.Convert(5.0, typeof(GridLength), null!, null!);
        var tooLow = (GridLength)converter.Convert(-5.0, typeof(GridLength), null!, null!);

        Assert.Equal(1.0, tooHigh.Value, precision: 3);
        Assert.Equal(0.0001, tooLow.Value, precision: 3);
    }
}
