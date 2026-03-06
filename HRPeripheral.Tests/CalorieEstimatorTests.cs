using Xunit;
using HRPeripheral;

namespace HRPeripheral.Tests;

public class CalorieEstimatorTests
{
    // =====================================================================
    // DefaultMale factory
    // =====================================================================

    [Fact]
    public void DefaultMale_At150bpm_ReturnsPositive()
    {
        var est = CalorieEstimator.DefaultMale75kgAge35();
        double result = est.KcalPerMinute(150);
        Assert.True(result > 0, $"Expected positive kcal/min at 150 bpm, got {result}");
    }

    [Fact]
    public void DefaultMale_IsMale75kg35()
    {
        var def = CalorieEstimator.DefaultMale75kgAge35();
        var manual = new CalorieEstimator(true, 75, 35);
        Assert.Equal(manual.KcalPerMinute(120), def.KcalPerMinute(120));
    }

    // =====================================================================
    // Male formula verification
    // =====================================================================

    [Fact]
    public void Male_KnownValue()
    {
        var est = new CalorieEstimator(true, 75, 35);
        double expected = (-55.0969 + 0.6309 * 150 + 0.1988 * 75 + 0.2017 * 35) / 4.184;
        Assert.Equal(expected, est.KcalPerMinute(150), precision: 6);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(100)]
    [InlineData(120)]
    [InlineData(180)]
    [InlineData(200)]
    public void Male_FormulaMatchesManualCalculation(int hr)
    {
        var est = new CalorieEstimator(true, 80, 40);
        double expected = (-55.0969 + 0.6309 * hr + 0.1988 * 80 + 0.2017 * 40) / 4.184;
        Assert.Equal(expected, est.KcalPerMinute(hr), precision: 6);
    }

    // =====================================================================
    // Female formula verification
    // =====================================================================

    [Fact]
    public void Female_KnownValue()
    {
        var est = new CalorieEstimator(false, 60, 28);
        double expected = (-20.4022 + 0.4472 * 120 - 0.1263 * 60 + 0.074 * 28) / 4.184;
        Assert.Equal(expected, est.KcalPerMinute(120), precision: 6);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(100)]
    [InlineData(120)]
    [InlineData(180)]
    [InlineData(200)]
    public void Female_FormulaMatchesManualCalculation(int hr)
    {
        var est = new CalorieEstimator(false, 55, 30);
        double expected = (-20.4022 + 0.4472 * hr - 0.1263 * 55 + 0.074 * 30) / 4.184;
        Assert.Equal(expected, est.KcalPerMinute(hr), precision: 6);
    }

    // =====================================================================
    // Male vs Female comparison
    // =====================================================================

    [Fact]
    public void MaleAndFemale_DifferentResults()
    {
        var male = new CalorieEstimator(true, 70, 30);
        var female = new CalorieEstimator(false, 70, 30);
        Assert.NotEqual(male.KcalPerMinute(150), female.KcalPerMinute(150));
    }

    // =====================================================================
    // Edge case HR values
    // =====================================================================

    [Fact]
    public void ZeroHr_DoesNotThrow()
    {
        var est = new CalorieEstimator(true, 100, 50);
        double result = est.KcalPerMinute(0);
        Assert.True(double.IsFinite(result));
    }

    [Fact]
    public void NegativeHr_DoesNotThrow()
    {
        var est = new CalorieEstimator(true, 75, 35);
        double result = est.KcalPerMinute(-10);
        Assert.True(double.IsFinite(result));
    }

    [Fact]
    public void VeryLowHr_DoesNotThrow()
    {
        var est = CalorieEstimator.DefaultMale75kgAge35();
        double result = est.KcalPerMinute(30);
        Assert.True(double.IsFinite(result));
    }

    [Fact]
    public void VeryHighHr_DoesNotThrow()
    {
        var est = new CalorieEstimator(true, 75, 35);
        double result = est.KcalPerMinute(300);
        Assert.True(double.IsFinite(result));
    }

    [Fact]
    public void MaxIntHr_DoesNotThrow()
    {
        var est = new CalorieEstimator(true, 75, 35);
        double result = est.KcalPerMinute(int.MaxValue);
        Assert.True(double.IsFinite(result));
    }

    // =====================================================================
    // Higher HR yields more calories
    // =====================================================================

    [Fact]
    public void Male_HigherHr_MoreCalories()
    {
        var est = new CalorieEstimator(true, 75, 35);
        Assert.True(est.KcalPerMinute(180) > est.KcalPerMinute(120));
    }

    [Fact]
    public void Female_HigherHr_MoreCalories()
    {
        var est = new CalorieEstimator(false, 60, 30);
        Assert.True(est.KcalPerMinute(180) > est.KcalPerMinute(120));
    }

    // =====================================================================
    // Weight effect
    // =====================================================================

    [Fact]
    public void Male_HeavierPerson_MoreCalories()
    {
        var light = new CalorieEstimator(true, 60, 35);
        var heavy = new CalorieEstimator(true, 100, 35);
        Assert.True(heavy.KcalPerMinute(150) > light.KcalPerMinute(150));
    }

    [Fact]
    public void Female_HeavierPerson_LessCalories()
    {
        // Female formula has negative weight coefficient (-0.1263)
        var light = new CalorieEstimator(false, 50, 30);
        var heavy = new CalorieEstimator(false, 90, 30);
        Assert.True(heavy.KcalPerMinute(150) < light.KcalPerMinute(150));
    }

    // =====================================================================
    // Extreme weight/age values
    // =====================================================================

    [Fact]
    public void ZeroWeight_DoesNotThrow()
    {
        var est = new CalorieEstimator(true, 0, 35);
        double result = est.KcalPerMinute(150);
        Assert.True(double.IsFinite(result));
    }

    [Fact]
    public void VeryLargeWeight_DoesNotThrow()
    {
        var est = new CalorieEstimator(true, 500, 35);
        double result = est.KcalPerMinute(150);
        Assert.True(double.IsFinite(result));
    }

    [Fact]
    public void ZeroAge_DoesNotThrow()
    {
        var est = new CalorieEstimator(true, 75, 0);
        double result = est.KcalPerMinute(150);
        Assert.True(double.IsFinite(result));
    }

    [Fact]
    public void NegativeAge_DoesNotThrow()
    {
        var est = new CalorieEstimator(false, 60, -5);
        double result = est.KcalPerMinute(120);
        Assert.True(double.IsFinite(result));
    }

    // =====================================================================
    // Consistency: same inputs produce same output
    // =====================================================================

    [Fact]
    public void SameInputs_ProduceSameOutput()
    {
        var est = new CalorieEstimator(true, 75, 35);
        double a = est.KcalPerMinute(150);
        double b = est.KcalPerMinute(150);
        Assert.Equal(a, b);
    }

    [Fact]
    public void TwoIdenticalEstimators_ProduceSameOutput()
    {
        var a = new CalorieEstimator(false, 65, 28);
        var b = new CalorieEstimator(false, 65, 28);
        Assert.Equal(a.KcalPerMinute(140), b.KcalPerMinute(140));
    }
}
