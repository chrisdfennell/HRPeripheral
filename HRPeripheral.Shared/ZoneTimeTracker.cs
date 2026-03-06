namespace HRPeripheral;

/// <summary>
/// Tracks cumulative time spent in each HR zone during a session.
/// Call Tick() periodically (e.g., every HR update) with the current zone.
/// Pure C#, no Android dependencies.
/// </summary>
public class ZoneTimeTracker
{
    private readonly TimeSpan[] _zoneTimes = new TimeSpan[5]; // index 0=zone1, etc.
    private DateTime _lastTick = DateTime.MinValue;
    private int _lastZone;

    /// <summary>
    /// Records a tick at the given zone. Call this each time a new HR sample arrives.
    /// </summary>
    public void Tick(int zone, DateTime now)
    {
        if (_lastTick != DateTime.MinValue && _lastZone >= 1 && _lastZone <= 5)
        {
            var elapsed = now - _lastTick;
            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromSeconds(10))
                _zoneTimes[_lastZone - 1] += elapsed;
        }
        _lastZone = zone;
        _lastTick = now;
    }

    /// <summary>Returns a copy of the zone times array (index 0 = Zone 1, etc.).</summary>
    public TimeSpan[] GetZoneTimes() => (TimeSpan[])_zoneTimes.Clone();

    /// <summary>Resets all tracked times.</summary>
    public void Reset()
    {
        Array.Clear(_zoneTimes, 0, _zoneTimes.Length);
        _lastTick = DateTime.MinValue;
        _lastZone = 0;
    }
}
