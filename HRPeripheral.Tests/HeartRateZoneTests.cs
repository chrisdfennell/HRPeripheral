using Xunit;

namespace HRPeripheral.Tests;

public class HeartRateZoneTests
{
    [Theory]
    [InlineData(35, 185)]  // 220 - 35
    [InlineData(15, 205)]  // 220 - 15
    [InlineData(80, 140)]  // 220 - 80
    public void MaxHr_CalculatesCorrectly(int age, int expected)
    {
        Assert.Equal(expected, HeartRateZone.MaxHr(age));
    }

    [Fact]
    public void MaxHr_ClampsAge()
    {
        Assert.Equal(HeartRateZone.MaxHr(15), HeartRateZone.MaxHr(10));  // 10 clamped to 15
        Assert.Equal(HeartRateZone.MaxHr(80), HeartRateZone.MaxHr(99));  // 99 clamped to 80
    }

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
        // 50/185 = 27% -> below zone 1
        Assert.Equal(0, HeartRateZone.GetZone(50, 35));
    }

    [Theory]
    [InlineData(1, 35, 92, 111)]   // Z1: 50-60% of 185 (92.5 rounds to 92)
    [InlineData(5, 35, 166, 185)]  // Z5: 90-100% of 185 (166.5 rounds to 166)
    [InlineData(3, 20, 140, 160)]  // Z3: 70-80% of 200
    public void GetZoneBpmRange_ReturnsCorrectBounds(int zone, int age, int expectedLow, int expectedHigh)
    {
        var (low, high) = HeartRateZone.GetZoneBpmRange(zone, age);
        Assert.Equal(expectedLow, low);
        Assert.Equal(expectedHigh, high);
    }

    [Fact]
    public void GetZoneBpmRange_InvalidZone_ReturnsZero()
    {
        Assert.Equal((0, 0), HeartRateZone.GetZoneBpmRange(0, 35));
        Assert.Equal((0, 0), HeartRateZone.GetZoneBpmRange(6, 35));
    }

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

    [Fact]
    public void GetZoneColor_InvalidZone_ReturnsGray()
    {
        var (r, g, b) = HeartRateZone.GetZoneColor(0);
        Assert.Equal((byte)0x80, r);
        Assert.Equal((byte)0x80, g);
        Assert.Equal((byte)0x80, b);
    }

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
}
