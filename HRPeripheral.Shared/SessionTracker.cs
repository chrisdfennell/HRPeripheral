namespace HRPeripheral;

/// <summary>
/// Tracks session statistics: HR min/max/avg, calorie accumulation, duration.
/// Pure C#, no Android dependencies.
/// </summary>
public class SessionTracker
{
    private readonly CalorieEstimator _cal;
    private readonly int _age;
    private readonly ZoneTimeTracker _zoneTracker = new();

    private int _hrMin = int.MaxValue;
    private int _hrMax;
    private long _hrSum;
    private int _hrCount;

    public DateTime StartTime { get; private set; }
    public double TotalKcal { get; private set; }
    public int HrMin => _hrCount > 0 ? _hrMin : 0;
    public int HrMax => _hrMax;
    public int HrAvg => _hrCount > 0 ? (int)Math.Round((double)_hrSum / _hrCount) : 0;
    public int SampleCount => _hrCount;

    public SessionTracker(CalorieEstimator cal, int age)
    {
        _cal = cal;
        _age = age;
        StartTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a heart rate sample. Updates stats, calories, and zone time.
    /// </summary>
    public void RecordHr(int bpm, DateTime now)
    {
        if (bpm <= 0) return;

        if (bpm < _hrMin) _hrMin = bpm;
        if (bpm > _hrMax) _hrMax = bpm;
        _hrSum += bpm;
        _hrCount++;

        TotalKcal += _cal.KcalPerMinute(bpm) / 60.0;

        int zone = HeartRateZone.GetZone(bpm, _age);
        _zoneTracker.Tick(zone, now);
    }

    /// <summary>Returns the current zone times (index 0 = Zone 1).</summary>
    public TimeSpan[] GetZoneTimes() => _zoneTracker.GetZoneTimes();

    /// <summary>Current session elapsed time.</summary>
    public TimeSpan Elapsed(DateTime now) => now - StartTime;

    /// <summary>
    /// Formats the elapsed duration as "MM:SS" or "H:MM:SS".
    /// </summary>
    public static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        return $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    /// <summary>Resets all session data and starts fresh.</summary>
    public void Reset()
    {
        _hrMin = int.MaxValue;
        _hrMax = 0;
        _hrSum = 0;
        _hrCount = 0;
        TotalKcal = 0;
        _zoneTracker.Reset();
        StartTime = DateTime.UtcNow;
    }
}
