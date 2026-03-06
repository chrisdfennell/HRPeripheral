using Xunit;
using HRPeripheral;

namespace HRPeripheral.Tests;

public class HrpPrefsTests
{
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(15, 10)]
    public void ClampHoldOffset_ClampsProperly(int input, int expected)
    {
        Assert.Equal(expected, HrpPrefs.ClampHoldOffset(input));
    }

    [Theory]
    [InlineData(0, 5000)]
    [InlineData(5, 10000)]
    [InlineData(10, 15000)]
    public void HoldOffsetToMillis_ConvertsCorrectly(int offset, long expectedMs)
    {
        Assert.Equal(expectedMs, HrpPrefs.HoldOffsetToMillis(offset));
    }

    [Fact]
    public void HoldOffsetToMillis_ClampsNegativeInput()
    {
        Assert.Equal(5000, HrpPrefs.HoldOffsetToMillis(-3));
    }

    [Fact]
    public void HoldOffsetToMillis_ClampsOverflowInput()
    {
        Assert.Equal(15000, HrpPrefs.HoldOffsetToMillis(99));
    }

    [Theory]
    [InlineData(30, 40)]
    [InlineData(40, 40)]
    [InlineData(75, 75)]
    [InlineData(150, 150)]
    [InlineData(200, 150)]
    public void ClampWeight_ClampsProperly(int input, int expected)
    {
        Assert.Equal(expected, HrpPrefs.ClampWeight(input));
    }

    [Theory]
    [InlineData(10, 15)]
    [InlineData(15, 15)]
    [InlineData(35, 35)]
    [InlineData(80, 80)]
    [InlineData(99, 80)]
    public void ClampAge_ClampsProperly(int input, int expected)
    {
        Assert.Equal(expected, HrpPrefs.ClampAge(input));
    }
}
