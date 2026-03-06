using Xunit;

namespace HRPeripheral.Tests;

public class ZoneTimeTrackerTests
{
    [Fact]
    public void Tick_SingleTick_NoTimeAccumulated()
    {
        var tracker = new ZoneTimeTracker();
        tracker.Tick(3, DateTime.UtcNow);
        var times = tracker.GetZoneTimes();
        Assert.All(times, t => Assert.Equal(TimeSpan.Zero, t));
    }

    [Fact]
    public void Tick_TwoTicks_AccumulatesTimeInFirstZone()
    {
        var tracker = new ZoneTimeTracker();
        var t0 = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddSeconds(5);

        tracker.Tick(3, t0);
        tracker.Tick(3, t1);

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(5), times[2]); // zone 3 -> index 2
    }

    [Fact]
    public void Tick_ZoneChange_SplitsTime()
    {
        var tracker = new ZoneTimeTracker();
        var t0 = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        tracker.Tick(2, t0);
        tracker.Tick(4, t0.AddSeconds(3));
        tracker.Tick(4, t0.AddSeconds(8));

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(3), times[1]); // zone 2
        Assert.Equal(TimeSpan.FromSeconds(5), times[3]); // zone 4
    }

    [Fact]
    public void Tick_ZoneBelowOne_IgnoresTime()
    {
        var tracker = new ZoneTimeTracker();
        var t0 = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        tracker.Tick(0, t0);
        tracker.Tick(0, t0.AddSeconds(5));

        var times = tracker.GetZoneTimes();
        Assert.All(times, t => Assert.Equal(TimeSpan.Zero, t));
    }

    [Fact]
    public void Tick_LargeGap_IgnoresTime()
    {
        var tracker = new ZoneTimeTracker();
        var t0 = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        tracker.Tick(3, t0);
        tracker.Tick(3, t0.AddSeconds(15)); // >10s gap, should be ignored

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.Zero, times[2]);
    }

    [Fact]
    public void Reset_ClearsAllTimes()
    {
        var tracker = new ZoneTimeTracker();
        var t0 = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        tracker.Tick(3, t0);
        tracker.Tick(3, t0.AddSeconds(5));
        tracker.Reset();

        var times = tracker.GetZoneTimes();
        Assert.All(times, t => Assert.Equal(TimeSpan.Zero, t));
    }

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
    public void Tick_AllFiveZones_TracksIndependently()
    {
        var tracker = new ZoneTimeTracker();
        var t = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        for (int zone = 1; zone <= 5; zone++)
        {
            tracker.Tick(zone, t);
            t = t.AddSeconds(zone); // zone 1 = 1s, zone 2 = 2s, etc.
        }
        tracker.Tick(5, t); // final tick to close zone 5

        var times = tracker.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(1), times[0]); // zone 1
        Assert.Equal(TimeSpan.FromSeconds(2), times[1]); // zone 2
        Assert.Equal(TimeSpan.FromSeconds(3), times[2]); // zone 3
        Assert.Equal(TimeSpan.FromSeconds(4), times[3]); // zone 4
        Assert.Equal(TimeSpan.FromSeconds(5), times[4]); // zone 5
    }
}
