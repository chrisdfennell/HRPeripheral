using Xunit;
using HRPeripheral;

namespace HRPeripheral.Tests;

public class CalorieEstimatorTests
{
    [Fact]
    public void DefaultMale_At150bpm_ReturnsPositive()
    {
        var est = CalorieEstimator.DefaultMale75kgAge35();
        double result = est.KcalPerMinute(150);
        Assert.True(result > 0, $"Expected positive kcal/min at 150 bpm, got {result}");
    }

    [Fact]
    public void Male_KnownValue()
    {
        var est = new CalorieEstimator(true, 75, 35);
        double expected = (-55.0969 + 0.6309 * 150 + 0.1988 * 75 + 0.2017 * 35) / 4.184;
        Assert.Equal(expected, est.KcalPerMinute(150), precision: 6);
    }

    [Fact]
    public void Female_KnownValue()
    {
        var est = new CalorieEstimator(false, 60, 28);
        double expected = (-20.4022 + 0.4472 * 120 - 0.1263 * 60 + 0.074 * 28) / 4.184;
        Assert.Equal(expected, est.KcalPerMinute(120), precision: 6);
    }

    [Fact]
    public void VeryLowHr_DoesNotThrow()
    {
        var est = CalorieEstimator.DefaultMale75kgAge35();
        double result = est.KcalPerMinute(30);
        Assert.True(double.IsFinite(result));
    }

    [Fact]
    public void ZeroHr_DoesNotThrow()
    {
        var est = new CalorieEstimator(true, 100, 50);
        double result = est.KcalPerMinute(0);
        Assert.True(double.IsFinite(result));
    }
}
