namespace HRPeripheral;

/// <summary>
/// Standard 5-zone heart rate model based on percentage of max HR.
/// Max HR = 220 - age. Pure C#, no Android dependencies.
/// </summary>
public static class HeartRateZone
{
    /// <summary>Zone number (1-5) with descriptive name and percentage range.</summary>
    public readonly record struct Zone(int Number, string Name, double LowPct, double HighPct);

    /// <summary>The five standard HR zones.</summary>
    public static readonly Zone[] Zones =
    [
        new Zone(1, "Very Light", 0.50, 0.60),
        new Zone(2, "Light",      0.60, 0.70),
        new Zone(3, "Moderate",   0.70, 0.80),
        new Zone(4, "Hard",       0.80, 0.90),
        new Zone(5, "Maximum",    0.90, 1.00),
    ];

    /// <summary>Computes max HR using the standard 220-age formula.</summary>
    public static int MaxHr(int age)
    {
        age = HrpPrefs.ClampAge(age);
        return 220 - age;
    }

    /// <summary>
    /// Returns the zone number (1-5) for the given BPM and age.
    /// Returns 0 if BPM is below zone 1 threshold.
    /// </summary>
    public static int GetZone(int bpm, int age)
    {
        int maxHr = MaxHr(age);
        double pct = (double)bpm / maxHr;

        for (int i = Zones.Length - 1; i >= 0; i--)
        {
            if (pct >= Zones[i].LowPct)
                return Zones[i].Number;
        }
        return 0;
    }

    /// <summary>
    /// Returns the BPM thresholds for a given zone and age.
    /// </summary>
    public static (int LowBpm, int HighBpm) GetZoneBpmRange(int zoneNumber, int age)
    {
        if (zoneNumber < 1 || zoneNumber > 5) return (0, 0);
        var z = Zones[zoneNumber - 1];
        int maxHr = MaxHr(age);
        return ((int)Math.Round(z.LowPct * maxHr), (int)Math.Round(z.HighPct * maxHr));
    }

    /// <summary>
    /// Returns a display color (R, G, B) for the given zone.
    /// Zone 1=blue, 2=green, 3=yellow, 4=orange, 5=red.
    /// </summary>
    public static (byte R, byte G, byte B) GetZoneColor(int zoneNumber) => zoneNumber switch
    {
        1 => (0x42, 0xA5, 0xF5), // Blue
        2 => (0x66, 0xBB, 0x6A), // Green
        3 => (0xFF, 0xEE, 0x58), // Yellow
        4 => (0xFF, 0xA7, 0x26), // Orange
        5 => (0xEF, 0x53, 0x50), // Red
        _ => (0x80, 0x80, 0x80), // Gray fallback
    };
}
