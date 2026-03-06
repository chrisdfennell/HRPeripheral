using Xunit;

namespace HRPeripheral.Tests;

public class HeartRateZoneTests
{
    // =====================================================================
    // MaxHr
    // =====================================================================

    [Theory]
    [InlineData(15, 205)]
    [InlineData(20, 200)]
    [InlineData(25, 195)]
    [InlineData(30, 190)]
    [InlineData(35, 185)]
    [InlineData(40, 180)]
    [InlineData(50, 170)]
    [InlineData(60, 160)]
    [InlineData(70, 150)]
    [InlineData(80, 140)]
    public void MaxHr_CalculatesCorrectly(int age, int expected)
    {
        Assert.Equal(expected, HeartRateZone.MaxHr(age));
    }

    [Fact]
    public void MaxHr_ClampsAgeBelowMin()
    {
        Assert.Equal(HeartRateZone.MaxHr(15), HeartRateZone.MaxHr(0));
        Assert.Equal(HeartRateZone.MaxHr(15), HeartRateZone.MaxHr(-5));
        Assert.Equal(HeartRateZone.MaxHr(15), HeartRateZone.MaxHr(10));
        Assert.Equal(HeartRateZone.MaxHr(15), HeartRateZone.MaxHr(14));
        Assert.Equal(HeartRateZone.MaxHr(15), HeartRateZone.MaxHr(int.MinValue));
    }

    [Fact]
    public void MaxHr_ClampsAgeAboveMax()
    {
        Assert.Equal(HeartRateZone.MaxHr(80), HeartRateZone.MaxHr(81));
        Assert.Equal(HeartRateZone.MaxHr(80), HeartRateZone.MaxHr(99));
        Assert.Equal(HeartRateZone.MaxHr(80), HeartRateZone.MaxHr(200));
        Assert.Equal(HeartRateZone.MaxHr(80), HeartRateZone.MaxHr(int.MaxValue));
    }

    [Fact]
    public void MaxHr_AlwaysPositive()
    {
        for (int age = 0; age <= 100; age++)
            Assert.True(HeartRateZone.MaxHr(age) > 0);
    }

    // =====================================================================
    // GetZone - standard zones
    // =====================================================================

    [Theory]
    [InlineData(93, 35, 1)]    // 93/185 = 50.3% -> zone 1
    [InlineData(120, 35, 2)]   // 120/185 = 64.9% -> zone 2
    [InlineData(130, 35, 3)]   // 130/185 = 70.3% -> zone 3
    [InlineData(150, 35, 4)]   // 150/185 = 81.1% -> zone 4
    [InlineData(170, 35, 5)]   // 170/185 = 91.9% -> zone 5
    [InlineData(185, 35, 5)]   // 185/185 = 100% -> zone 5
    [InlineData(200, 35, 5)]   // above max -> still zone 5
    public void GetZone_ReturnsCorrectZone(int bpm, int age, int expectedZone)
    {
        Assert.Equal(expectedZone, HeartRateZone.GetZone(bpm, age));
    }

    [Fact]
    public void GetZone_BelowAllZones_ReturnsZero()
    {
        Assert.Equal(0, HeartRateZone.GetZone(50, 35));  // 50/185 = 27%
    }

    [Fact]
    public void GetZone_ZeroBpm_ReturnsZero()
    {
        Assert.Equal(0, HeartRateZone.GetZone(0, 35));
    }

    [Fact]
    public void GetZone_NegativeBpm_ReturnsZero()
    {
        Assert.Equal(0, HeartRateZone.GetZone(-10, 35));
    }

    [Fact]
    public void GetZone_VeryHighBpm_ReturnsZone5()
    {
        Assert.Equal(5, HeartRateZone.GetZone(300, 35));
        Assert.Equal(5, HeartRateZone.GetZone(int.MaxValue, 35));
    }

    // =====================================================================
    // GetZone - zone boundary precision
    // =====================================================================

    [Fact]
    public void GetZone_ExactlyAtZone1LowBoundary()
    {
        // maxHR=185, 50% = 92.5 => bpm of 92 is 49.7% (zone 0), 93 is 50.3% (zone 1)
        Assert.Equal(0, HeartRateZone.GetZone(92, 35));
        Assert.Equal(1, HeartRateZone.GetZone(93, 35));
    }

    [Fact]
    public void GetZone_Zone1ToZone2Boundary()
    {
        // maxHR=185, 60% = 111 => 110/185=59.5% (zone 1), 111/185=60% (zone 2)
        Assert.Equal(1, HeartRateZone.GetZone(110, 35));
        Assert.Equal(2, HeartRateZone.GetZone(111, 35));
    }

    [Fact]
    public void GetZone_Zone2ToZone3Boundary()
    {
        // maxHR=185, 70% = 129.5 => 129/185=69.7% (zone 2), 130/185=70.3% (zone 3)
        Assert.Equal(2, HeartRateZone.GetZone(129, 35));
        Assert.Equal(3, HeartRateZone.GetZone(130, 35));
    }

    [Fact]
    public void GetZone_Zone3ToZone4Boundary()
    {
        // maxHR=185, 80% = 148 => 147/185=79.5% (zone 3), 148/185=80% (zone 4)
        Assert.Equal(3, HeartRateZone.GetZone(147, 35));
        Assert.Equal(4, HeartRateZone.GetZone(148, 35));
    }

    [Fact]
    public void GetZone_Zone4ToZone5Boundary()
    {
        // maxHR=185, 90% = 166.5 => 166/185=89.7% (zone 4), 167/185=90.3% (zone 5)
        Assert.Equal(4, HeartRateZone.GetZone(166, 35));
        Assert.Equal(5, HeartRateZone.GetZone(167, 35));
    }

    // =====================================================================
    // GetZone - various ages
    // =====================================================================

    [Theory]
    [InlineData(15)]
    [InlineData(25)]
    [InlineData(45)]
    [InlineData(65)]
    [InlineData(80)]
    public void GetZone_AllAges_Zone5AtMaxHr(int age)
    {
        int maxHr = HeartRateZone.MaxHr(age);
        Assert.Equal(5, HeartRateZone.GetZone(maxHr, age));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(25)]
    [InlineData(45)]
    [InlineData(65)]
    [InlineData(80)]
    public void GetZone_AllAges_RestingHrIsZone0(int age)
    {
        Assert.Equal(0, HeartRateZone.GetZone(60, age));
    }

    // =====================================================================
    // GetZoneBpmRange
    // =====================================================================

    [Theory]
    [InlineData(1, 35, 92, 111)]
    [InlineData(2, 35, 111, 130)]
    [InlineData(3, 35, 130, 148)]
    [InlineData(4, 35, 148, 166)]
    [InlineData(5, 35, 166, 185)]
    public void GetZoneBpmRange_ReturnsCorrectBounds(int zone, int age, int expectedLow, int expectedHigh)
    {
        var (low, high) = HeartRateZone.GetZoneBpmRange(zone, age);
        Assert.Equal(expectedLow, low);
        Assert.Equal(expectedHigh, high);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(100)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void GetZoneBpmRange_InvalidZone_ReturnsZero(int zone)
    {
        Assert.Equal((0, 0), HeartRateZone.GetZoneBpmRange(zone, 35));
    }

    [Fact]
    public void GetZoneBpmRange_Zone5High_EqualsMaxHr()
    {
        int age = 35;
        var (_, high) = HeartRateZone.GetZoneBpmRange(5, age);
        Assert.Equal(HeartRateZone.MaxHr(age), high);
    }

    [Fact]
    public void GetZoneBpmRange_AllZones_LowLessThanHigh()
    {
        for (int zone = 1; zone <= 5; zone++)
        {
            var (low, high) = HeartRateZone.GetZoneBpmRange(zone, 35);
            Assert.True(low < high, $"Zone {zone}: low={low} should be < high={high}");
        }
    }

    [Fact]
    public void GetZoneBpmRange_DifferentAges_ProduceDifferentRanges()
    {
        var (low20, high20) = HeartRateZone.GetZoneBpmRange(3, 20);
        var (low60, high60) = HeartRateZone.GetZoneBpmRange(3, 60);
        Assert.True(low20 > low60);
        Assert.True(high20 > high60);
    }

    // =====================================================================
    // GetZoneColor
    // =====================================================================

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void GetZoneColor_ValidZones_ReturnsNonGray(int zone)
    {
        var (r, g, b) = HeartRateZone.GetZoneColor(zone);
        Assert.False(r == 0x80 && g == 0x80 && b == 0x80, "Expected non-gray color for valid zone");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(100)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void GetZoneColor_InvalidZone_ReturnsGray(int zone)
    {
        var (r, g, b) = HeartRateZone.GetZoneColor(zone);
        Assert.Equal((byte)0x80, r);
        Assert.Equal((byte)0x80, g);
        Assert.Equal((byte)0x80, b);
    }

    [Fact]
    public void GetZoneColor_EachZoneHasUniqueColor()
    {
        var colors = new HashSet<(byte, byte, byte)>();
        for (int z = 1; z <= 5; z++)
        {
            var color = HeartRateZone.GetZoneColor(z);
            Assert.True(colors.Add(color), $"Zone {z} has duplicate color");
        }
    }

    [Fact]
    public void GetZoneColor_Deterministic()
    {
        for (int z = 1; z <= 5; z++)
        {
            var a = HeartRateZone.GetZoneColor(z);
            var b = HeartRateZone.GetZoneColor(z);
            Assert.Equal(a, b);
        }
    }

    // =====================================================================
    // Zones array structure
    // =====================================================================

    [Fact]
    public void Zones_AreFive()
    {
        Assert.Equal(5, HeartRateZone.Zones.Length);
    }

    [Fact]
    public void Zones_AreConsecutive()
    {
        for (int i = 0; i < HeartRateZone.Zones.Length; i++)
            Assert.Equal(i + 1, HeartRateZone.Zones[i].Number);
    }

    [Fact]
    public void Zones_PercentsAreContiguous()
    {
        for (int i = 1; i < HeartRateZone.Zones.Length; i++)
            Assert.Equal(HeartRateZone.Zones[i - 1].HighPct, HeartRateZone.Zones[i].LowPct);
    }

    [Fact]
    public void Zones_FirstStartsAt50Percent()
    {
        Assert.Equal(0.50, HeartRateZone.Zones[0].LowPct);
    }

    [Fact]
    public void Zones_LastEndsAt100Percent()
    {
        Assert.Equal(1.00, HeartRateZone.Zones[^1].HighPct);
    }

    [Fact]
    public void Zones_AllHaveNames()
    {
        foreach (var zone in HeartRateZone.Zones)
            Assert.False(string.IsNullOrWhiteSpace(zone.Name), $"Zone {zone.Number} has no name");
    }

    [Fact]
    public void Zones_LowPctLessThanHighPct()
    {
        foreach (var zone in HeartRateZone.Zones)
            Assert.True(zone.LowPct < zone.HighPct, $"Zone {zone.Number}: LowPct >= HighPct");
    }
}
