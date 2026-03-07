using Xunit;

namespace HRPeripheral.Tests;

/// <summary>
/// Integration-style tests that exercise multiple shared components together
/// in realistic session workflows. Tests cover end-to-end data flow from
/// HR payload parsing through session tracking, zone classification,
/// calorie estimation, and reconnect backoff.
/// </summary>
public class IntegrationTests
{
    // =====================================================================
    // Full session workflow: parse -> track -> zones -> calories -> summary
    // =====================================================================

    [Fact]
    public void FullSession_ParseAndTrack_ProducesCorrectSummary()
    {
        // Simulate a 5-minute session with varying HR
        var cal = new CalorieEstimator(true, 80, 30);
        var session = new SessionTracker(cal, 30);
        var start = new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        int[] hrValues = { 70, 85, 100, 120, 140, 155, 170, 160, 145, 130, 110, 90, 75 };

        for (int i = 0; i < hrValues.Length; i++)
        {
            // Build a BLE payload and parse it back (round-trip)
            var payload = HrPayload.Build(hrValues[i]);
            int parsed = HrPayload.Parse(payload);
            Assert.Equal(hrValues[i], parsed);

            // Use 5-second intervals so ZoneTimeTracker records time (< 10s threshold)
            session.RecordHr(parsed, start.AddSeconds(i * 5));
        }

        Assert.Equal(70, session.HrMin);
        Assert.Equal(170, session.HrMax);
        Assert.Equal(hrValues.Length, session.SampleCount);
        Assert.True(session.TotalKcal > 0);

        // Zone times should have data in multiple zones
        var zoneTimes = session.GetZoneTimes();
        int zonesWithTime = zoneTimes.Count(z => z > TimeSpan.Zero);
        Assert.True(zonesWithTime >= 2, "Should have time in at least 2 different zones");
    }

    [Fact]
    public void FullSession_ZoneDistribution_MatchesExpected()
    {
        // Age 30 => maxHR = 190
        // Zone thresholds: Z1=95, Z2=114, Z3=133, Z4=152, Z5=171
        var cal = new CalorieEstimator(true, 75, 30);
        var session = new SessionTracker(cal, 30);
        var t = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 10 seconds in Zone 2 (120 bpm = 63% of 190)
        session.RecordHr(120, t);
        session.RecordHr(120, t.AddSeconds(5));
        session.RecordHr(120, t.AddSeconds(10));

        // 10 seconds in Zone 4 (160 bpm = 84% of 190)
        session.RecordHr(160, t.AddSeconds(15));
        session.RecordHr(160, t.AddSeconds(20));
        session.RecordHr(160, t.AddSeconds(25));

        var zones = session.GetZoneTimes();
        Assert.True(zones[1].TotalSeconds > 0, "Zone 2 should have time"); // index 1 = zone 2
        Assert.True(zones[3].TotalSeconds > 0, "Zone 4 should have time"); // index 3 = zone 4
    }

    // =====================================================================
    // Session reset and new session
    // =====================================================================

    [Fact]
    public void SessionReset_FollowedByNewSession_TracksSeparately()
    {
        var cal = new CalorieEstimator(true, 75, 35);
        var session = new SessionTracker(cal, 35);
        var t = DateTime.UtcNow;

        // First session
        session.RecordHr(150, t);
        session.RecordHr(160, t.AddSeconds(1));
        double firstKcal = session.TotalKcal;
        int firstMax = session.HrMax;

        // Reset
        session.Reset();

        // Second session - lower effort
        session.RecordHr(80, t.AddSeconds(10));
        session.RecordHr(85, t.AddSeconds(11));

        Assert.Equal(80, session.HrMin);
        Assert.Equal(85, session.HrMax);
        Assert.True(session.TotalKcal < firstKcal, "Lower HR session should have fewer calories");
        Assert.True(session.HrMax < firstMax);
    }

    // =====================================================================
    // BLE payload round-trip tests
    // =====================================================================

    [Theory]
    [InlineData(60)]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    [InlineData(255)]
    public void HrPayload_RoundTrip_PreservesValue(int bpm)
    {
        var payload = HrPayload.Build(bpm);
        int parsed = HrPayload.Parse(payload);
        Assert.Equal(bpm, parsed);
    }

    [Fact]
    public void HrPayload_16BitFormat_ParsesCorrectly()
    {
        // Simulate a 16-bit HR payload (flags bit 0 = 1)
        int bpm = 300; // > 255, needs 16-bit
        byte flags = 0x01;
        byte lo = (byte)(bpm & 0xFF);
        byte hi = (byte)((bpm >> 8) & 0xFF);
        var payload = new byte[] { flags, lo, hi };

        int parsed = HrPayload.Parse(payload);
        Assert.Equal(bpm, parsed);
    }

    [Fact]
    public void HrPayload_ParsedIntoSession_CalculatesCalories()
    {
        var cal = new CalorieEstimator(true, 80, 30);
        var session = new SessionTracker(cal, 30);

        // Simulate receiving 60 BLE payloads over 60 seconds (1 per second)
        var t = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 60; i++)
        {
            var payload = HrPayload.Build(140);
            int hr = HrPayload.Parse(payload);
            session.RecordHr(hr, t.AddSeconds(i));
        }

        // ~1 minute at 140 bpm for an 80kg, 30yo male should give reasonable calories
        // CalorieEstimator formula: (-55.0969 + 0.6309*140 + 0.1988*80 + 0.2017*30) / 4.184
        double expectedKcalPerMin = cal.KcalPerMinute(140);
        // Each RecordHr adds kcalPerMin/60, so 60 samples ≈ 1 minute
        double expectedTotal = expectedKcalPerMin; // 60 * (kcalPerMin / 60)

        Assert.InRange(session.TotalKcal, expectedTotal * 0.95, expectedTotal * 1.05);
    }

    // =====================================================================
    // Reconnect backoff with session continuity
    // =====================================================================

    [Fact]
    public void ReconnectBackoff_SimulateDisconnectReconnectCycle()
    {
        var backoff = new ReconnectBackoff(1000, 30000);

        // First disconnect
        int delay1 = backoff.NextDelayMs();
        Assert.Equal(1000, delay1);
        Assert.Equal(1, backoff.Attempt);

        // Still disconnected
        int delay2 = backoff.NextDelayMs();
        Assert.Equal(2000, delay2);

        int delay3 = backoff.NextDelayMs();
        Assert.Equal(4000, delay3);

        // Reconnected!
        backoff.Reset();
        Assert.Equal(0, backoff.Attempt);

        // Disconnect again - should start fresh
        int delay4 = backoff.NextDelayMs();
        Assert.Equal(1000, delay4);
    }

    [Fact]
    public void ReconnectBackoff_CappedAtMax()
    {
        var backoff = new ReconnectBackoff(1000, 8000);

        backoff.NextDelayMs(); // 1000
        backoff.NextDelayMs(); // 2000
        backoff.NextDelayMs(); // 4000
        int delay = backoff.NextDelayMs(); // should be capped at 8000
        Assert.Equal(8000, delay);

        int delay2 = backoff.NextDelayMs(); // still 8000
        Assert.Equal(8000, delay2);
    }

    // =====================================================================
    // Calorie estimator gender comparison
    // =====================================================================

    [Fact]
    public void CalorieEstimator_MaleVsFemale_DifferentResults()
    {
        var male = new CalorieEstimator(true, 75, 30);
        var female = new CalorieEstimator(false, 75, 30);

        double maleKcal = male.KcalPerMinute(140);
        double femaleKcal = female.KcalPerMinute(140);

        Assert.NotEqual(maleKcal, femaleKcal);
        Assert.True(maleKcal > 0);
        Assert.True(femaleKcal > 0);
    }

    // =====================================================================
    // Zone classification with different ages
    // =====================================================================

    [Theory]
    [InlineData(20, 160, 4)] // maxHR=200, 160/200=80% => zone 4
    [InlineData(40, 160, 4)] // maxHR=180, 160/180=89% => zone 4
    [InlineData(60, 140, 4)] // maxHR=160, 140/160=87.5% => zone 4
    public void ZoneClassification_VariesByAge(int age, int bpm, int minExpectedZone)
    {
        int zone = HeartRateZone.GetZone(bpm, age);
        Assert.True(zone >= minExpectedZone,
            $"Expected zone >= {minExpectedZone} for age={age}, bpm={bpm}, got zone={zone}");
    }

    [Fact]
    public void ZoneClassification_SameBpm_HigherAge_HigherZone()
    {
        // Same HR but older person has lower maxHR, so higher zone
        int zone25 = HeartRateZone.GetZone(150, 25); // maxHR=195, 150/195=77%
        int zone55 = HeartRateZone.GetZone(150, 55); // maxHR=165, 150/165=91%

        Assert.True(zone55 > zone25,
            $"Expected older person to be in higher zone: age25={zone25}, age55={zone55}");
    }

    // =====================================================================
    // End-to-end: simulate a real workout session
    // =====================================================================

    [Fact]
    public void SimulateWorkout_WarmupSteadyStateCooldown()
    {
        var cal = new CalorieEstimator(true, 80, 35);
        var session = new SessionTracker(cal, 35);
        var t = new DateTime(2025, 6, 1, 7, 0, 0, DateTimeKind.Utc);

        // Warmup: 5 minutes at ~100 bpm (every 5 seconds)
        for (int i = 0; i < 60; i++)
            session.RecordHr(95 + (i % 10), t.AddSeconds(i * 5));

        // Steady state: 20 minutes at ~155 bpm
        var steadyStart = t.AddMinutes(5);
        for (int i = 0; i < 240; i++)
            session.RecordHr(150 + (i % 10), steadyStart.AddSeconds(i * 5));

        // Cooldown: 5 minutes at ~90 bpm
        var cooldownStart = t.AddMinutes(25);
        for (int i = 0; i < 60; i++)
            session.RecordHr(85 + (i % 10), cooldownStart.AddSeconds(i * 5));

        // Verify comprehensive stats
        Assert.Equal(360, session.SampleCount); // 60 + 240 + 60
        Assert.True(session.HrMin <= 95);
        Assert.True(session.HrMax >= 155);
        Assert.True(session.TotalKcal > 50, "30-min workout should burn > 50 kcal");
        Assert.True(session.TotalKcal < 500, "30-min workout should burn < 500 kcal");

        // Zone distribution
        var zones = session.GetZoneTimes();
        int zonesUsed = zones.Count(z => z > TimeSpan.Zero);
        Assert.True(zonesUsed >= 2, $"Expected >= 2 zones used, got {zonesUsed}");

        // Duration formatting (tested separately; just verify it doesn't throw)
        var thirtyMin = TimeSpan.FromMinutes(30);
        Assert.Equal("30:00", SessionTracker.FormatDuration(thirtyMin));
    }
}
