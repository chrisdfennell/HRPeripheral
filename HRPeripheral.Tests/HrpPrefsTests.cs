using Xunit;
using HRPeripheral;

namespace HRPeripheral.Tests;

public class HrpPrefsTests
{
    // =====================================================================
    // ClampHoldOffset
    // =====================================================================

    [Theory]
    [InlineData(-100, 0)]
    [InlineData(-5, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(9, 9)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    [InlineData(50, 10)]
    [InlineData(int.MinValue, 0)]
    [InlineData(int.MaxValue, 10)]
    public void ClampHoldOffset_ClampsProperly(int input, int expected)
    {
        Assert.Equal(expected, HrpPrefs.ClampHoldOffset(input));
    }

    // =====================================================================
    // HoldOffsetToMillis
    // =====================================================================

    [Theory]
    [InlineData(0, 5000)]
    [InlineData(1, 6000)]
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

    [Fact]
    public void HoldOffsetToMillis_IntMinValue()
    {
        Assert.Equal(5000, HrpPrefs.HoldOffsetToMillis(int.MinValue));
    }

    [Fact]
    public void HoldOffsetToMillis_IntMaxValue()
    {
        Assert.Equal(15000, HrpPrefs.HoldOffsetToMillis(int.MaxValue));
    }

    // =====================================================================
    // ClampWeight
    // =====================================================================

    [Theory]
    [InlineData(int.MinValue, 40)]
    [InlineData(-1, 40)]
    [InlineData(0, 40)]
    [InlineData(30, 40)]
    [InlineData(39, 40)]
    [InlineData(40, 40)]
    [InlineData(41, 41)]
    [InlineData(75, 75)]
    [InlineData(149, 149)]
    [InlineData(150, 150)]
    [InlineData(151, 150)]
    [InlineData(200, 150)]
    [InlineData(int.MaxValue, 150)]
    public void ClampWeight_ClampsProperly(int input, int expected)
    {
        Assert.Equal(expected, HrpPrefs.ClampWeight(input));
    }

    // =====================================================================
    // ClampAge
    // =====================================================================

    [Theory]
    [InlineData(int.MinValue, 15)]
    [InlineData(-1, 15)]
    [InlineData(0, 15)]
    [InlineData(10, 15)]
    [InlineData(14, 15)]
    [InlineData(15, 15)]
    [InlineData(16, 16)]
    [InlineData(35, 35)]
    [InlineData(79, 79)]
    [InlineData(80, 80)]
    [InlineData(81, 80)]
    [InlineData(99, 80)]
    [InlineData(int.MaxValue, 80)]
    public void ClampAge_ClampsProperly(int input, int expected)
    {
        Assert.Equal(expected, HrpPrefs.ClampAge(input));
    }

    // =====================================================================
    // ClampStillWindow
    // =====================================================================

    [Theory]
    [InlineData(int.MinValue, 5)]
    [InlineData(-1, 5)]
    [InlineData(0, 5)]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(15, 15)]
    [InlineData(30, 30)]
    [InlineData(59, 59)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    [InlineData(100, 60)]
    [InlineData(int.MaxValue, 60)]
    public void ClampStillWindow_ClampsProperly(int input, int expected)
    {
        Assert.Equal(expected, HrpPrefs.ClampStillWindow(input));
    }

    // =====================================================================
    // ClampWatchdogResume
    // =====================================================================

    [Theory]
    [InlineData(int.MinValue, 5)]
    [InlineData(-1, 5)]
    [InlineData(0, 5)]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(15, 15)]
    [InlineData(30, 30)]
    [InlineData(59, 59)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    [InlineData(100, 60)]
    [InlineData(int.MaxValue, 60)]
    public void ClampWatchdogResume_ClampsProperly(int input, int expected)
    {
        Assert.Equal(expected, HrpPrefs.ClampWatchdogResume(input));
    }

    // =====================================================================
    // Constants consistency
    // =====================================================================

    [Fact]
    public void DefaultHoldOffset_IsWithinRange()
    {
        Assert.InRange(HrpPrefs.DEFAULT_HOLD_OFFSET, HrpPrefs.MIN_HOLD_OFFSET, HrpPrefs.MAX_HOLD_OFFSET);
    }

    [Fact]
    public void DefaultWeight_IsWithinRange()
    {
        Assert.InRange(HrpPrefs.DEFAULT_CAL_WEIGHT_KG, HrpPrefs.MIN_CAL_WEIGHT_KG, HrpPrefs.MAX_CAL_WEIGHT_KG);
    }

    [Fact]
    public void DefaultAge_IsWithinRange()
    {
        Assert.InRange(HrpPrefs.DEFAULT_CAL_AGE, HrpPrefs.MIN_CAL_AGE, HrpPrefs.MAX_CAL_AGE);
    }

    [Fact]
    public void DefaultStillWindow_IsWithinRange()
    {
        Assert.InRange(HrpPrefs.DEFAULT_STILL_WINDOW_S, HrpPrefs.MIN_STILL_WINDOW_S, HrpPrefs.MAX_STILL_WINDOW_S);
    }

    [Fact]
    public void DefaultWatchdogResume_IsWithinRange()
    {
        Assert.InRange(HrpPrefs.DEFAULT_WATCHDOG_RESUME_S, HrpPrefs.MIN_WATCHDOG_RESUME_S, HrpPrefs.MAX_WATCHDOG_RESUME_S);
    }

    [Fact]
    public void MinValues_AreLessThanOrEqualToMax()
    {
        Assert.True(HrpPrefs.MIN_HOLD_OFFSET <= HrpPrefs.MAX_HOLD_OFFSET);
        Assert.True(HrpPrefs.MIN_CAL_WEIGHT_KG <= HrpPrefs.MAX_CAL_WEIGHT_KG);
        Assert.True(HrpPrefs.MIN_CAL_AGE <= HrpPrefs.MAX_CAL_AGE);
        Assert.True(HrpPrefs.MIN_STILL_WINDOW_S <= HrpPrefs.MAX_STILL_WINDOW_S);
        Assert.True(HrpPrefs.MIN_WATCHDOG_RESUME_S <= HrpPrefs.MAX_WATCHDOG_RESUME_S);
    }

    // =====================================================================
    // Clamp idempotency
    // =====================================================================

    [Theory]
    [InlineData(-100)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(100)]
    public void ClampHoldOffset_IsIdempotent(int input)
    {
        int first = HrpPrefs.ClampHoldOffset(input);
        Assert.Equal(first, HrpPrefs.ClampHoldOffset(first));
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(40)]
    [InlineData(75)]
    [InlineData(150)]
    [InlineData(300)]
    public void ClampWeight_IsIdempotent(int input)
    {
        int first = HrpPrefs.ClampWeight(input);
        Assert.Equal(first, HrpPrefs.ClampWeight(first));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(15)]
    [InlineData(35)]
    [InlineData(80)]
    [InlineData(200)]
    public void ClampAge_IsIdempotent(int input)
    {
        int first = HrpPrefs.ClampAge(input);
        Assert.Equal(first, HrpPrefs.ClampAge(first));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(999)]
    public void ClampStillWindow_IsIdempotent(int input)
    {
        int first = HrpPrefs.ClampStillWindow(input);
        Assert.Equal(first, HrpPrefs.ClampStillWindow(first));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(999)]
    public void ClampWatchdogResume_IsIdempotent(int input)
    {
        int first = HrpPrefs.ClampWatchdogResume(input);
        Assert.Equal(first, HrpPrefs.ClampWatchdogResume(first));
    }
}
