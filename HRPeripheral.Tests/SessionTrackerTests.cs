using Xunit;

namespace HRPeripheral.Tests;

public class SessionTrackerTests
{
    private static SessionTracker MakeTracker() =>
        new(new CalorieEstimator(true, 75, 35), 35);

    [Fact]
    public void NoSamples_StatsAreZero()
    {
        var s = MakeTracker();
        Assert.Equal(0, s.HrMin);
        Assert.Equal(0, s.HrMax);
        Assert.Equal(0, s.HrAvg);
        Assert.Equal(0, s.SampleCount);
        Assert.Equal(0.0, s.TotalKcal);
    }

    [Fact]
    public void SingleSample_SetsMinMaxAvg()
    {
        var s = MakeTracker();
        s.RecordHr(120, DateTime.UtcNow);

        Assert.Equal(120, s.HrMin);
        Assert.Equal(120, s.HrMax);
        Assert.Equal(120, s.HrAvg);
        Assert.Equal(1, s.SampleCount);
    }

    [Fact]
    public void MultipleSamples_TracksMinMaxAvg()
    {
        var s = MakeTracker();
        var t = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        s.RecordHr(100, t);
        s.RecordHr(150, t.AddSeconds(1));
        s.RecordHr(130, t.AddSeconds(2));

        Assert.Equal(100, s.HrMin);
        Assert.Equal(150, s.HrMax);
        Assert.Equal(127, s.HrAvg); // (100+150+130)/3 = 126.67 -> 127
        Assert.Equal(3, s.SampleCount);
    }

    [Fact]
    public void RecordHr_AccumulatesCalories()
    {
        var s = MakeTracker();
        var t = DateTime.UtcNow;

        s.RecordHr(150, t);
        Assert.True(s.TotalKcal > 0, "Calories should be positive after recording HR");

        double afterFirst = s.TotalKcal;
        s.RecordHr(150, t.AddSeconds(1));
        Assert.True(s.TotalKcal > afterFirst, "Calories should increase with each sample");
    }

    [Fact]
    public void RecordHr_TracksZoneTimes()
    {
        var s = MakeTracker();
        var t = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // 150 bpm at age 35 => maxHR=185, 150/185=81% => zone 4
        s.RecordHr(150, t);
        s.RecordHr(150, t.AddSeconds(5));

        var zones = s.GetZoneTimes();
        Assert.Equal(TimeSpan.FromSeconds(5), zones[3]); // zone 4 = index 3
    }

    [Fact]
    public void RecordHr_IgnoresZeroBpm()
    {
        var s = MakeTracker();
        s.RecordHr(0, DateTime.UtcNow);

        Assert.Equal(0, s.SampleCount);
        Assert.Equal(0, s.HrMin);
    }

    [Fact]
    public void RecordHr_IgnoresNegativeBpm()
    {
        var s = MakeTracker();
        s.RecordHr(-5, DateTime.UtcNow);

        Assert.Equal(0, s.SampleCount);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var s = MakeTracker();
        var t = DateTime.UtcNow;

        s.RecordHr(120, t);
        s.RecordHr(150, t.AddSeconds(1));
        s.Reset();

        Assert.Equal(0, s.SampleCount);
        Assert.Equal(0, s.HrMin);
        Assert.Equal(0, s.HrMax);
        Assert.Equal(0, s.HrAvg);
        Assert.Equal(0.0, s.TotalKcal);

        var zones = s.GetZoneTimes();
        Assert.All(zones, z => Assert.Equal(TimeSpan.Zero, z));
    }

    [Theory]
    [InlineData(0, 30, "00:30")]
    [InlineData(5, 3, "05:03")]
    [InlineData(59, 59, "59:59")]
    public void FormatDuration_MinutesSeconds(int mins, int secs, string expected)
    {
        Assert.Equal(expected, SessionTracker.FormatDuration(new TimeSpan(0, mins, secs)));
    }

    [Theory]
    [InlineData(1, 5, 3, "1:05:03")]
    [InlineData(2, 0, 0, "2:00:00")]
    [InlineData(10, 30, 15, "10:30:15")]
    public void FormatDuration_HoursMinutesSeconds(int hrs, int mins, int secs, string expected)
    {
        Assert.Equal(expected, SessionTracker.FormatDuration(new TimeSpan(hrs, mins, secs)));
    }

    [Fact]
    public void Elapsed_ReflectsTimeSinceStart()
    {
        var s = MakeTracker();
        var now = s.StartTime.AddMinutes(5);
        var elapsed = s.Elapsed(now);
        Assert.Equal(5, elapsed.TotalMinutes, 1);
    }
}
