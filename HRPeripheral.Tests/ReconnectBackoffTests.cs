using Xunit;

namespace HRPeripheral.Tests;

public class ReconnectBackoffTests
{
    // =====================================================================
    // Constructor validation
    // =====================================================================

    [Fact]
    public void Constructor_ThrowsOnZeroInitial()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(0, 1000));
    }

    [Fact]
    public void Constructor_ThrowsOnNegativeInitial()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(-1, 1000));
    }

    [Fact]
    public void Constructor_ThrowsOnNegativeLargeInitial()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(int.MinValue, 1000));
    }

    [Fact]
    public void Constructor_ThrowsWhenMaxLessThanInitial()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(5000, 1000));
    }

    [Fact]
    public void Constructor_AcceptsMaxEqualToInitial()
    {
        var bo = new ReconnectBackoff(1000, 1000);
        Assert.Equal(1000, bo.NextDelayMs());
        Assert.Equal(1000, bo.NextDelayMs()); // can't double past max
    }

    [Fact]
    public void Constructor_AcceptsSmallValues()
    {
        var bo = new ReconnectBackoff(1, 1);
        Assert.Equal(1, bo.NextDelayMs());
    }

    // =====================================================================
    // Default constructor values
    // =====================================================================

    [Fact]
    public void DefaultConstructor_Has1000msInitial()
    {
        var bo = new ReconnectBackoff();
        Assert.Equal(1000, bo.NextDelayMs());
    }

    [Fact]
    public void DefaultConstructor_Has30sMax()
    {
        var bo = new ReconnectBackoff();
        // Advance past 30s: 1000, 2000, 4000, 8000, 16000, 32000->30000
        for (int i = 0; i < 5; i++) bo.NextDelayMs();
        Assert.Equal(30_000, bo.NextDelayMs()); // should be capped at 30000
    }

    // =====================================================================
    // Exponential doubling
    // =====================================================================

    [Fact]
    public void FirstDelay_ReturnsInitial()
    {
        var bo = new ReconnectBackoff(1000, 30_000);
        Assert.Equal(1000, bo.NextDelayMs());
    }

    [Fact]
    public void Doubles_EachCall()
    {
        var bo = new ReconnectBackoff(1000, 30_000);
        Assert.Equal(1000, bo.NextDelayMs());
        Assert.Equal(2000, bo.NextDelayMs());
        Assert.Equal(4000, bo.NextDelayMs());
        Assert.Equal(8000, bo.NextDelayMs());
    }

    [Fact]
    public void CapsAtMax()
    {
        var bo = new ReconnectBackoff(1000, 5000);
        Assert.Equal(1000, bo.NextDelayMs());
        Assert.Equal(2000, bo.NextDelayMs());
        Assert.Equal(4000, bo.NextDelayMs());
        Assert.Equal(5000, bo.NextDelayMs());
        Assert.Equal(5000, bo.NextDelayMs());
    }

    [Fact]
    public void CapsAtMax_StaysAtMax()
    {
        var bo = new ReconnectBackoff(1000, 5000);
        // Advance to max
        while (bo.CurrentDelayMs < 5000) bo.NextDelayMs();
        // Should stay at max for many calls
        for (int i = 0; i < 100; i++)
            Assert.Equal(5000, bo.NextDelayMs());
    }

    // =====================================================================
    // Reset
    // =====================================================================

    [Fact]
    public void Reset_RestoresInitial()
    {
        var bo = new ReconnectBackoff(1000, 30_000);
        bo.NextDelayMs();
        bo.NextDelayMs();
        bo.Reset();
        Assert.Equal(1000, bo.NextDelayMs());
    }

    [Fact]
    public void Reset_ClearsAttempt()
    {
        var bo = new ReconnectBackoff(500, 10_000);
        bo.NextDelayMs();
        bo.NextDelayMs();
        bo.Reset();
        Assert.Equal(0, bo.Attempt);
    }

    [Fact]
    public void Reset_CanBeCalledMultipleTimes()
    {
        var bo = new ReconnectBackoff(1000, 30_000);
        bo.NextDelayMs();
        bo.Reset();
        bo.NextDelayMs();
        bo.Reset();
        bo.Reset();
        Assert.Equal(0, bo.Attempt);
        Assert.Equal(1000, bo.CurrentDelayMs);
    }

    [Fact]
    public void Reset_AfterCap_RestoresFullSequence()
    {
        var bo = new ReconnectBackoff(1000, 4000);
        bo.NextDelayMs(); // 1000
        bo.NextDelayMs(); // 2000
        bo.NextDelayMs(); // 4000 (capped)
        bo.NextDelayMs(); // 4000
        bo.Reset();
        Assert.Equal(1000, bo.NextDelayMs());
        Assert.Equal(2000, bo.NextDelayMs());
        Assert.Equal(4000, bo.NextDelayMs());
    }

    // =====================================================================
    // Attempt tracking
    // =====================================================================

    [Fact]
    public void Attempt_StartsAtZero()
    {
        var bo = new ReconnectBackoff(500, 10_000);
        Assert.Equal(0, bo.Attempt);
    }

    [Fact]
    public void Attempt_TracksCallCount()
    {
        var bo = new ReconnectBackoff(500, 10_000);
        for (int i = 1; i <= 20; i++)
        {
            bo.NextDelayMs();
            Assert.Equal(i, bo.Attempt);
        }
    }

    [Fact]
    public void Attempt_ContinuesCountingPastCap()
    {
        var bo = new ReconnectBackoff(1000, 2000);
        bo.NextDelayMs(); // 1
        bo.NextDelayMs(); // 2
        bo.NextDelayMs(); // 3 (delay capped but attempt keeps counting)
        bo.NextDelayMs(); // 4
        Assert.Equal(4, bo.Attempt);
    }

    // =====================================================================
    // CurrentDelayMs
    // =====================================================================

    [Fact]
    public void CurrentDelayMs_PeeksWithoutAdvancing()
    {
        var bo = new ReconnectBackoff(1000, 30_000);
        Assert.Equal(1000, bo.CurrentDelayMs);
        Assert.Equal(1000, bo.CurrentDelayMs);
        bo.NextDelayMs();
        Assert.Equal(2000, bo.CurrentDelayMs);
    }

    [Fact]
    public void CurrentDelayMs_MatchesNextDelayMs()
    {
        var bo = new ReconnectBackoff(1000, 30_000);
        for (int i = 0; i < 10; i++)
        {
            int current = bo.CurrentDelayMs;
            int next = bo.NextDelayMs();
            Assert.Equal(current, next);
        }
    }

    [Fact]
    public void CurrentDelayMs_AfterReset()
    {
        var bo = new ReconnectBackoff(1000, 30_000);
        bo.NextDelayMs();
        bo.NextDelayMs();
        bo.Reset();
        Assert.Equal(1000, bo.CurrentDelayMs);
    }

    // =====================================================================
    // Various initial/max combinations
    // =====================================================================

    [Fact]
    public void SmallInitial_LargeMax()
    {
        var bo = new ReconnectBackoff(1, 1_000_000);
        Assert.Equal(1, bo.NextDelayMs());
        Assert.Equal(2, bo.NextDelayMs());
        Assert.Equal(4, bo.NextDelayMs());
    }

    [Fact]
    public void OddMax_CapsCorrectly()
    {
        var bo = new ReconnectBackoff(1000, 3000);
        Assert.Equal(1000, bo.NextDelayMs());
        Assert.Equal(2000, bo.NextDelayMs());
        Assert.Equal(3000, bo.NextDelayMs()); // 4000 capped to 3000
        Assert.Equal(3000, bo.NextDelayMs());
    }
}
