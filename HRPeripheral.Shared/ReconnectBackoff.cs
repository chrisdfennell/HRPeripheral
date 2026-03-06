namespace HRPeripheral;

/// <summary>
/// Pure C# exponential backoff calculator for BLE reconnection.
/// Doubles the delay on each failure, capped at a maximum.
/// </summary>
public class ReconnectBackoff
{
    private readonly int _initialDelayMs;
    private readonly int _maxDelayMs;
    private int _currentDelayMs;

    public ReconnectBackoff(int initialDelayMs = 1000, int maxDelayMs = 30_000)
    {
        if (initialDelayMs <= 0) throw new ArgumentOutOfRangeException(nameof(initialDelayMs));
        if (maxDelayMs < initialDelayMs) throw new ArgumentOutOfRangeException(nameof(maxDelayMs));

        _initialDelayMs = initialDelayMs;
        _maxDelayMs = maxDelayMs;
        _currentDelayMs = initialDelayMs;
    }

    /// <summary>Returns the current delay and advances to the next (doubled, capped).</summary>
    public int NextDelayMs()
    {
        int delay = _currentDelayMs;
        _currentDelayMs = Math.Min(_currentDelayMs * 2, _maxDelayMs);
        Attempt++;
        return delay;
    }

    /// <summary>Current delay without advancing.</summary>
    public int CurrentDelayMs => _currentDelayMs;

    /// <summary>Resets the backoff to the initial delay (call on successful connection).</summary>
    public void Reset()
    {
        _currentDelayMs = _initialDelayMs;
        Attempt = 0;
    }

    /// <summary>Number of times NextDelayMs has been called since last reset.</summary>
    public int Attempt { get; private set; }
}
