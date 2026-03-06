using Xunit;

namespace HRPeripheral.Tests;

public class ZoneTimeTrackerTests
{
    private static readonly DateTime T0 = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    // =====================================================================
    // Initial state
    // =====================================================================

    [Fact]
    public void NewTracker_AllZerosTimesAreZero()
    {
        var tracker = new ZoneTimeTracker();
        var times = tracker.GetZoneTimes();
        Assert.Equal(5, times.Length);
        Assert.All(times, t => Assert.Equal(TimeSpan.Zero, t));
    }

    // =====================================================================
    // Single tick (no pair to accumulate)
    // =====================================================================

    [Fact]
    public void Tick_SingleTick_NoTimeAccumulated()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(3, DateTime.UtcNow);
        var times = tracker.GetZoneTimes();
        Assert.All(times, t => Assert.Equal(TimeSpan.Zero, t));
    }

    // =====================================================================
    // Two ticks in same zone
    // =====================================================================

    [Fact]
    public void Tick_TwoTicks_AccumulatesTimeInFirstZone()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(3, T0);
        tracker.Tick(3, T0.AddSeconds(5));

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(5), times[2]); // zone 3 -> index 2
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 4)]
    public void Tick_TwoTicks_EachZone_CorrectIndex(int zone, int expectedIndex)
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(zone, T0);
        tracker.Tick(zone, T0.AddSeconds(3));

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(3), times[expectedIndex]);

        // All other zones should be zero
        for (int i = 0; i < 5; i++)
        {
            if (i != expectedIndex)
                Assert.Equal(TimeSpan.Zero, times[i]);
        }
    }

    // =====================================================================
    // Zone changes
    // =====================================================================

    [Fact]
    public void Tick_ZoneChange_SplitsTime()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(2, T0);
        tracker.Tick(4, T0.AddSeconds(3));
        tracker.Tick(4, T0.AddSeconds(8));

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(3), times[1]); // zone 2
        Assert.Equal(TimeSpan.FromSeconds(5), times[3]); // zone 4
    }

    [Fact]
    public void Tick_RapidZoneChanges()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(1, T0);
        tracker.Tick(2, T0.AddSeconds(1));
        tracker.Tick(3, T0.AddSeconds(2));
        tracker.Tick(4, T0.AddSeconds(3));
        tracker.Tick(5, T0.AddSeconds(4));
        tracker.Tick(5, T0.AddSeconds(5));

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(1), times[0]);
        Assert.Equal(TimeSpan.FromSeconds(1), times[1]);
        Assert.Equal(TimeSpan.FromSeconds(1), times[2]);
        Assert.Equal(TimeSpan.FromSeconds(1), times[3]);
        Assert.Equal(TimeSpan.FromSeconds(1), times[4]);
    }

    [Fact]
    public void Tick_BackAndForth_AccumulatesCorrectly()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(1, T0);
        tracker.Tick(3, T0.AddSeconds(2)); // 2s in zone 1
        tracker.Tick(1, T0.AddSeconds(5)); // 3s in zone 3
        tracker.Tick(3, T0.AddSeconds(8)); // 3s in zone 1

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(5), times[0]); // zone 1: 2+3
        Assert.Equal(TimeSpan.FromSeconds(3), times[2]); // zone 3: 3
    }

    // =====================================================================
    // Zone 0 (below all zones)
    // =====================================================================

    [Fact]
    public void Tick_ZoneBelowOne_IgnoresTime()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(0, T0);
        tracker.Tick(0, T0.AddSeconds(5));

        var times = tracker.GetZoneTimes();
        Assert.All(times, t => Assert.Equal(TimeSpan.Zero, t));
    }

    [Fact]
    public void Tick_NegativeZone_IgnoresTime()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(-1, T0);
        tracker.Tick(-1, T0.AddSeconds(5));

        var times = tracker.GetZoneTimes();
        Assert.All(times, t => Assert.Equal(TimeSpan.Zero, t));
    }

    [Fact]
    public void Tick_ZoneAboveFive_IgnoresTime()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(6, T0);
        tracker.Tick(6, T0.AddSeconds(5));

        var times = tracker.GetZoneTimes();
        Assert.All(times, t => Assert.Equal(TimeSpan.Zero, t));
    }

    [Fact]
    public void Tick_TransitionFromZone0ToValid_OnlyCountsValid()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(0, T0);           // zone 0 tick
        tracker.Tick(3, T0.AddSeconds(3));  // zone 0 -> zone 3 (zone 0 time discarded)
        tracker.Tick(3, T0.AddSeconds(6));  // 3s in zone 3

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(3), times[2]); // only the 3->3 interval
    }

    [Fact]
    public void Tick_TransitionFromValidToZone0()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(3, T0);
        tracker.Tick(0, T0.AddSeconds(4)); // 4s in zone 3
        tracker.Tick(0, T0.AddSeconds(8)); // zone 0, no time accumulated

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(4), times[2]); // zone 3: 4s
    }

    // =====================================================================
    // Large gap (>10s) handling
    // =====================================================================

    [Fact]
    public void Tick_LargeGap_IgnoresTime()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(3, T0);
        tracker.Tick(3, T0.AddSeconds(15)); // >10s gap

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.Zero, times[2]);
    }

    [Fact]
    public void Tick_ExactlyTenSeconds_AccumulatesTime()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(3, T0);
        tracker.Tick(3, T0.AddSeconds(10)); // exactly 10s should be ignored (< 10 not <=10)

        var times = tracker.GetZoneTimes();
        // The threshold is "elapsed < TimeSpan.FromSeconds(10)", so exactly 10 is NOT accumulated
        Assert.Equal(TimeSpan.Zero, times[2]);
    }

    [Fact]
    public void Tick_JustUnderTenSeconds_AccumulatesTime()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(3, T0);
        tracker.Tick(3, T0.AddMilliseconds(9999)); // just under 10s

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromMilliseconds(9999), times[2]);
    }

    [Fact]
    public void Tick_LargeGap_ThenNormalGap_OnlyCountsNormal()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(3, T0);
        tracker.Tick(3, T0.AddSeconds(20)); // gap too large, ignored
        tracker.Tick(3, T0.AddSeconds(25)); // 5s from previous, normal

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(5), times[2]);
    }

    // =====================================================================
    // Negative elapsed time
    // =====================================================================

    [Fact]
    public void Tick_BackwardsTime_IgnoresInterval()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(3, T0);
        tracker.Tick(3, T0.AddSeconds(-1)); // time went backwards

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.Zero, times[2]); // elapsed <= 0 not accumulated
    }

    [Fact]
    public void Tick_SameTime_IgnoresInterval()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(3, T0);
        tracker.Tick(3, T0); // exactly same time

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.Zero, times[2]); // elapsed = 0, not > 0
    }

    // =====================================================================
    // Reset
    // =====================================================================

    [Fact]
    public void Reset_ClearsAllTimes()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(3, T0);
        tracker.Tick(3, T0.AddSeconds(5));
        tracker.Reset();

        var times = tracker.GetZoneTimes();
        Assert.All(times, t => Assert.Equal(TimeSpan.Zero, t));
    }

    [Fact]
    public void Reset_NextTickStartsFresh()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(2, T0);
        tracker.Tick(2, T0.AddSeconds(5));
        tracker.Reset();

        // After reset, single tick should not accumulate (no prior tick)
        tracker.Tick(4, T0.AddSeconds(10));
        var times = tracker.GetZoneTimes();
        Assert.All(times, t => Assert.Equal(TimeSpan.Zero, t));

        // Second tick after reset should accumulate
        tracker.Tick(4, T0.AddSeconds(13));
        times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(3), times[3]);
    }

    [Fact]
    public void Reset_CalledMultipleTimes()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(1, T0);
        tracker.Tick(1, T0.AddSeconds(3));
        tracker.Reset();
        tracker.Reset();
        tracker.Reset();

        var times = tracker.GetZoneTimes();
        Assert.All(times, t => Assert.Equal(TimeSpan.Zero, t));
    }

    // =====================================================================
    // GetZoneTimes returns a copy
    // =====================================================================

    [Fact]
    public void GetZoneTimes_ReturnsCopy()
    {
        var tracker = new ZoneTimeTracker();
        var times1 = tracker.GetZoneTimes();
        times1[0] = TimeSpan.FromHours(999);
        var times2 = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.Zero, times2[0]);
    }

    [Fact]
    public void GetZoneTimes_AlwaysReturnsNewArray()
    {
        var tracker = new ZoneTimeTracker();
        var a = tracker.GetZoneTimes();
        var b = tracker.GetZoneTimes();
        Assert.NotSame(a, b);
    }

    // =====================================================================
    // All zones tracked independently
    // =====================================================================

    [Fact]
    public void Tick_AllFiveZones_TracksIndependently()
    {
        var tracker = new ZoneTimeTracker();
        var t = T0;

        for (int zone = 1; zone <= 5; zone++)
        {
            tracker.Tick(zone, t);
            t = t.AddSeconds(zone);
        }
        tracker.Tick(5, t); // final tick to close zone 5

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(1), times[0]);
        Assert.Equal(TimeSpan.FromSeconds(2), times[1]);
        Assert.Equal(TimeSpan.FromSeconds(3), times[2]);
        Assert.Equal(TimeSpan.FromSeconds(4), times[3]);
        Assert.Equal(TimeSpan.FromSeconds(5), times[4]);
    }

    // =====================================================================
    // Long session accumulation
    // =====================================================================

    [Fact]
    public void Tick_ManySamples_AccumulatesCorrectly()
    {
        var tracker = new ZoneTimeTracker();
        var t = T0;

        // 60 ticks, 1 second apart, all in zone 3
        for (int i = 0; i < 60; i++)
        {
            tracker.Tick(3, t);
            t = t.AddSeconds(1);
        }
        tracker.Tick(3, t); // final tick

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(60), times[2]);
    }

    // =====================================================================
    // Sub-second precision
    // =====================================================================

    [Fact]
    public void Tick_SubSecondIntervals_AccumulateCorrectly()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(1, T0);
        tracker.Tick(1, T0.AddMilliseconds(500));
        tracker.Tick(1, T0.AddMilliseconds(1000));

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromMilliseconds(1000), times[0]);
    }
}
