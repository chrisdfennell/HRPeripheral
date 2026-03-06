using Xunit;

namespace HRPeripheral.Tests;

public class ReconnectBackoffTests
{
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
        Assert.Equal(1000, bo.NextDelayMs());  // -> 2000
        Assert.Equal(2000, bo.NextDelayMs());  // -> 4000
        Assert.Equal(4000, bo.NextDelayMs());  // -> 5000 (capped)
        Assert.Equal(5000, bo.NextDelayMs());  // stays 5000
        Assert.Equal(5000, bo.NextDelayMs());
    }

    [Fact]
    public void Reset_RestoresInitial()
    {
        var bo = new ReconnectBackoff(1000, 30_000);
        bo.NextDelayMs(); // 1000
        bo.NextDelayMs(); // 2000
        bo.Reset();
        Assert.Equal(1000, bo.NextDelayMs());
    }

    [Fact]
    public void Attempt_TracksCallCount()
    {
        var bo = new ReconnectBackoff(500, 10_000);
        Assert.Equal(0, bo.Attempt);
        bo.NextDelayMs();
        Assert.Equal(1, bo.Attempt);
        bo.NextDelayMs();
        Assert.Equal(2, bo.Attempt);
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
    public void CurrentDelayMs_PeeksWithoutAdvancing()
    {
        var bo = new ReconnectBackoff(1000, 30_000);
        Assert.Equal(1000, bo.CurrentDelayMs);
        Assert.Equal(1000, bo.CurrentDelayMs); // still 1000, not advanced
        bo.NextDelayMs(); // consume 1000, advance to 2000
        Assert.Equal(2000, bo.CurrentDelayMs);
    }

    [Fact]
    public void Constructor_ThrowsOnInvalidArgs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(0, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(-1, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(5000, 1000));
    }
}
