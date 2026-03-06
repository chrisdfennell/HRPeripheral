using Xunit;

namespace HRPeripheral.Tests;

public class SessionTrackerTests
{
    private static SessionTracker MakeTracker() =>
        new(new CalorieEstimator(true, 75, 35), 35);

    private static SessionTracker MakeTracker(bool male, int weightKg, int age) =>
        new(new CalorieEstimator(male, weightKg, age), age);

    // =====================================================================
    // Initial state
    // =====================================================================

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
    public void NoSamples_ZoneTimesAllZero()
    {
        var s = MakeTracker();
        var zones = s.GetZoneTimes();
        Assert.Equal(5, zones.Length);
        Assert.All(zones, z => Assert.Equal(TimeSpan.Zero, z));
    }

    [Fact]
    public void StartTime_IsSet()
    {
        var before = DateTime.UtcNow;
        var s = MakeTracker();
        var after = DateTime.UtcNow;
        Assert.InRange(s.StartTime, before, after);
    }

    // =====================================================================
    // Single sample
    // =====================================================================

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
    public void SingleSample_1bpm()
    {
        var s = MakeTracker();
        s.RecordHr(1, DateTime.UtcNow);
        Assert.Equal(1, s.HrMin);
        Assert.Equal(1, s.HrMax);
        Assert.Equal(1, s.SampleCount);
    }

    [Fact]
    public void SingleSample_255bpm()
    {
        var s = MakeTracker();
        s.RecordHr(255, DateTime.UtcNow);
        Assert.Equal(255, s.HrMin);
        Assert.Equal(255, s.HrMax);
        Assert.Equal(1, s.SampleCount);
    }

    // =====================================================================
    // Multiple samples
    // =====================================================================

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
    public void MultipleSamples_MinDecreases()
    {
        var s = MakeTracker();
        var t = DateTime.UtcNow;
        s.RecordHr(120, t);
        Assert.Equal(120, s.HrMin);
        s.RecordHr(80, t.AddSeconds(1));
        Assert.Equal(80, s.HrMin);
    }

    [Fact]
    public void MultipleSamples_MaxIncreases()
    {
        var s = MakeTracker();
        var t = DateTime.UtcNow;
        s.RecordHr(80, t);
        Assert.Equal(80, s.HrMax);
        s.RecordHr(160, t.AddSeconds(1));
        Assert.Equal(160, s.HrMax);
    }

    [Fact]
    public void ManySamples_AvgIsCorrect()
    {
        var s = MakeTracker();
        var t = DateTime.UtcNow;
        int sum = 0;
        for (int i = 0; i < 100; i++)
        {
            int bpm = 60 + i;
            s.RecordHr(bpm, t.AddSeconds(i));
            sum += bpm;
        }
        int expectedAvg = (int)Math.Round((double)sum / 100);
        Assert.Equal(expectedAvg, s.HrAvg);
        Assert.Equal(100, s.SampleCount);
    }

    [Fact]
    public void AvgRounding_HalfUp()
    {
        var s = MakeTracker();
        var t = DateTime.UtcNow;
        // Two samples: 100 + 101 = 201 / 2 = 100.5 -> rounds to 100 or 101
        s.RecordHr(100, t);
        s.RecordHr(101, t.AddSeconds(1));
        // Math.Round uses banker's rounding by default: 100.5 -> 100 (even)
        Assert.Equal((int)Math.Round(100.5), s.HrAvg);
    }

    // =====================================================================
    // Calorie accumulation
    // =====================================================================

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
    public void RecordHr_CaloriesScale_PerSample()
    {
        var s = MakeTracker();
        var t = DateTime.UtcNow;
        s.RecordHr(150, t);
        double oneKcal = s.TotalKcal;

        s.RecordHr(150, t.AddSeconds(1));
        double twoKcal = s.TotalKcal;

        Assert.Equal(oneKcal * 2, twoKcal, precision: 6);
    }

    [Fact]
    public void RecordHr_HigherHr_MoreCalories()
    {
        var s1 = MakeTracker();
        var s2 = MakeTracker();
        var t = DateTime.UtcNow;

        s1.RecordHr(100, t);
        s2.RecordHr(180, t);

        Assert.True(s2.TotalKcal > s1.TotalKcal);
    }

    // =====================================================================
    // Zone time tracking
    // =====================================================================

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
    public void RecordHr_LowBpm_Zone0_NoZoneTimeTracked()
    {
        var s = MakeTracker();
        var t = DateTime.UtcNow;

        // 50 bpm at age 35 => 50/185=27% => zone 0
        s.RecordHr(50, t);
        s.RecordHr(50, t.AddSeconds(5));

        var zones = s.GetZoneTimes();
        Assert.All(zones, z => Assert.Equal(TimeSpan.Zero, z));
    }

    [Fact]
    public void GetZoneTimes_Returns5Elements()
    {
        var s = MakeTracker();
        Assert.Equal(5, s.GetZoneTimes().Length);
    }

    // =====================================================================
    // Ignoring invalid BPM
    // =====================================================================

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
    public void RecordHr_IgnoresNegativeBpm_NoCalories()
    {
        var s = MakeTracker();
        s.RecordHr(-100, DateTime.UtcNow);
        Assert.Equal(0.0, s.TotalKcal);
    }

    [Fact]
    public void RecordHr_MixOfValidAndInvalid()
    {
        var s = MakeTracker();
        var t = DateTime.UtcNow;

        s.RecordHr(0, t);
        s.RecordHr(-1, t.AddSeconds(1));
        s.RecordHr(120, t.AddSeconds(2));
        s.RecordHr(0, t.AddSeconds(3));
        s.RecordHr(130, t.AddSeconds(4));

        Assert.Equal(2, s.SampleCount);
        Assert.Equal(120, s.HrMin);
        Assert.Equal(130, s.HrMax);
    }

    // =====================================================================
    // Reset
    // =====================================================================

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

    [Fact]
    public void Reset_ResetsStartTime()
    {
        var s = MakeTracker();
        var originalStart = s.StartTime;
        System.Threading.Thread.Sleep(10);
        s.Reset();
        Assert.True(s.StartTime > originalStart);
    }

    [Fact]
    public void Reset_AllowsNewData()
    {
        var s = MakeTracker();
        var t = DateTime.UtcNow;

        s.RecordHr(120, t);
        s.Reset();
        s.RecordHr(80, t.AddSeconds(10));

        Assert.Equal(80, s.HrMin);
        Assert.Equal(80, s.HrMax);
        Assert.Equal(1, s.SampleCount);
    }

    [Fact]
    public void Reset_CalledMultipleTimes()
    {
        var s = MakeTracker();
        s.RecordHr(100, DateTime.UtcNow);
        s.Reset();
        s.Reset();
        s.Reset();

        Assert.Equal(0, s.SampleCount);
        Assert.Equal(0.0, s.TotalKcal);
    }

    // =====================================================================
    // FormatDuration
    // =====================================================================

    [Theory]
    [InlineData(0, 0, "00:00")]
    [InlineData(0, 1, "00:01")]
    [InlineData(0, 30, "00:30")]
    [InlineData(0, 59, "00:59")]
    [InlineData(1, 0, "01:00")]
    [InlineData(5, 3, "05:03")]
    [InlineData(59, 59, "59:59")]
    public void FormatDuration_MinutesSeconds(int mins, int secs, string expected)
    {
        Assert.Equal(expected, SessionTracker.FormatDuration(new TimeSpan(0, mins, secs)));
    }

    [Theory]
    [InlineData(1, 0, 0, "1:00:00")]
    [InlineData(1, 5, 3, "1:05:03")]
    [InlineData(2, 0, 0, "2:00:00")]
    [InlineData(10, 30, 15, "10:30:15")]
    [InlineData(23, 59, 59, "23:59:59")]
    [InlineData(100, 0, 0, "100:00:00")]
    public void FormatDuration_HoursMinutesSeconds(int hrs, int mins, int secs, string expected)
    {
        Assert.Equal(expected, SessionTracker.FormatDuration(new TimeSpan(hrs, mins, secs)));
    }

    [Fact]
    public void FormatDuration_ZeroDuration()
    {
        Assert.Equal("00:00", SessionTracker.FormatDuration(TimeSpan.Zero));
    }

    [Fact]
    public void FormatDuration_ExactlyOneHour()
    {
        Assert.Equal("1:00:00", SessionTracker.FormatDuration(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void FormatDuration_JustUnderOneHour()
    {
        var ts = TimeSpan.FromMinutes(59).Add(TimeSpan.FromSeconds(59));
        Assert.Equal("59:59", SessionTracker.FormatDuration(ts));
    }

    // =====================================================================
    // Elapsed
    // =====================================================================

    [Fact]
    public void Elapsed_ReflectsTimeSinceStart()
    {
        var s = MakeTracker();
        var now = s.StartTime.AddMinutes(5);
        var elapsed = s.Elapsed(now);
        Assert.Equal(5, elapsed.TotalMinutes, 1);
    }

    [Fact]
    public void Elapsed_AtStartTime_IsZero()
    {
        var s = MakeTracker();
        var elapsed = s.Elapsed(s.StartTime);
        Assert.Equal(TimeSpan.Zero, elapsed);
    }

    [Fact]
    public void Elapsed_BeforeStartTime_IsNegative()
    {
        var s = MakeTracker();
        var elapsed = s.Elapsed(s.StartTime.AddMinutes(-1));
        Assert.True(elapsed < TimeSpan.Zero);
    }

    // =====================================================================
    // Female tracker
    // =====================================================================

    [Fact]
    public void FemaleTracker_AccumulatesCalories()
    {
        var s = MakeTracker(false, 60, 28);
        var t = DateTime.UtcNow;
        s.RecordHr(140, t);
        Assert.True(s.TotalKcal > 0);
    }
}
