namespace HRPeripheral;

/// <summary>
/// Centralized preference keys, defaults, and validation for the HRPeripheral app.
/// Shared by MainActivity, SettingsActivity, and HeartRateService to avoid
/// scattered string literals and duplicated clamping logic.
/// </summary>
public static class HrpPrefs
{
    // ================================================================
    // PREFERENCE FILE
    // ================================================================
    public const string PREFS_NAME = "hrp_prefs";

    // ================================================================
    // HOLD-TO-EXIT
    // ================================================================
    public const string KEY_HOLD_ENABLED = "hold_enabled";
    public const bool DEFAULT_HOLD_ENABLED = true;

    public const string KEY_HOLD_SECONDS = "hold_seconds"; // offset 0..10
    public const int DEFAULT_HOLD_OFFSET = 5;
    public const int MIN_HOLD_OFFSET = 0;
    public const int MAX_HOLD_OFFSET = 10;
    public const int BASE_HOLD_SECONDS = 5; // offset 0 => 5s, offset 10 => 15s

    /// <summary>Clamps a hold offset value to the valid range [0, 10].</summary>
    public static int ClampHoldOffset(int offset)
    {
        if (offset < MIN_HOLD_OFFSET) return MIN_HOLD_OFFSET;
        if (offset > MAX_HOLD_OFFSET) return MAX_HOLD_OFFSET;
        return offset;
    }

    /// <summary>
    /// Converts a hold offset (0..10) to the actual hold duration in milliseconds.
    /// Offset 0 => 5000ms, offset 10 => 15000ms.
    /// </summary>
    public static long HoldOffsetToMillis(int offset)
    {
        return (BASE_HOLD_SECONDS + ClampHoldOffset(offset)) * 1000L;
    }

    // ================================================================
    // AUTO-PAUSE
    // ================================================================
    public const string KEY_AUTO_PAUSE = "auto_pause";
    public const bool DEFAULT_AUTO_PAUSE = false;

    // ================================================================
    // CALORIE PROFILE
    // ================================================================
    public const string KEY_CAL_MALE = "cal_male";
    public const bool DEFAULT_CAL_MALE = true;

    public const string KEY_CAL_WEIGHT_KG = "cal_weight_kg";
    public const int DEFAULT_CAL_WEIGHT_KG = 75;
    public const int MIN_CAL_WEIGHT_KG = 40;
    public const int MAX_CAL_WEIGHT_KG = 150;

    public const string KEY_CAL_AGE = "cal_age";
    public const int DEFAULT_CAL_AGE = 35;
    public const int MIN_CAL_AGE = 15;
    public const int MAX_CAL_AGE = 80;

    // ================================================================
    // AUTO-PAUSE TIMING
    // ================================================================
    /// <summary>Seconds of no motion before auto-pause triggers.</summary>
    public const string KEY_STILL_WINDOW_S = "still_window_s";
    public const int DEFAULT_STILL_WINDOW_S = 15;
    public const int MIN_STILL_WINDOW_S = 5;
    public const int MAX_STILL_WINDOW_S = 60;

    /// <summary>Seconds of pause before watchdog forces a resume.</summary>
    public const string KEY_WATCHDOG_RESUME_S = "watchdog_resume_s";
    public const int DEFAULT_WATCHDOG_RESUME_S = 15;
    public const int MIN_WATCHDOG_RESUME_S = 5;
    public const int MAX_WATCHDOG_RESUME_S = 60;

    /// <summary>Clamp still-window seconds to valid range.</summary>
    public static int ClampStillWindow(int s)
    {
        if (s < MIN_STILL_WINDOW_S) return MIN_STILL_WINDOW_S;
        if (s > MAX_STILL_WINDOW_S) return MAX_STILL_WINDOW_S;
        return s;
    }

    /// <summary>Clamp watchdog-resume seconds to valid range.</summary>
    public static int ClampWatchdogResume(int s)
    {
        if (s < MIN_WATCHDOG_RESUME_S) return MIN_WATCHDOG_RESUME_S;
        if (s > MAX_WATCHDOG_RESUME_S) return MAX_WATCHDOG_RESUME_S;
        return s;
    }

    /// <summary>Clamp weight to valid range [40, 150] kg.</summary>
    public static int ClampWeight(int kg)
    {
        if (kg < MIN_CAL_WEIGHT_KG) return MIN_CAL_WEIGHT_KG;
        if (kg > MAX_CAL_WEIGHT_KG) return MAX_CAL_WEIGHT_KG;
        return kg;
    }

    /// <summary>Clamp age to valid range [15, 80].</summary>
    public static int ClampAge(int age)
    {
        if (age < MIN_CAL_AGE) return MIN_CAL_AGE;
        if (age > MAX_CAL_AGE) return MAX_CAL_AGE;
        return age;
    }
}
