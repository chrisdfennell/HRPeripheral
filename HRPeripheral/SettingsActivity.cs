using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace HRPeripheral;

/// <summary>
/// Activity that displays and saves user preferences for the HRPeripheral app.
///
/// Currently manages:
///  • Whether the “hold-to-measure” feature is enabled.
///  • How long the “hold” delay lasts (5–15 seconds adjustable by SeekBar).
///
/// All settings are persisted in SharedPreferences (`hrp_prefs`).
/// </summary>
[Activity(Label = "Settings", Theme = "@android:style/Theme.DeviceDefault")]
public class SettingsActivity : Activity
{
    // ================================================================
    // CONSTANTS
    // ================================================================
    // SharedPreferences file name
    private const string PREFS = "hrp_prefs";

    // Keys for individual stored settings
    private const string PREF_HOLD_ENABLED = "hold_enabled";
    private const string PREF_HOLD_SECONDS = "hold_seconds"; // offset 0..10 → maps to 5..15 seconds

    // ================================================================
    // UI ELEMENTS
    // ================================================================
    private Switch? _switchHold;     // Toggle switch to enable/disable “hold” feature
    private SeekBar? _seekHold;      // Slider for adjusting hold time offset (0–10)
    private TextView? _valueLabel;   // Displays the actual time (e.g., “8 seconds”)

    // ================================================================
    // LIFECYCLE: OnCreate
    // ================================================================
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.settings_activity);

        // Look up view references from layout
        _switchHold = FindViewById<Switch>(Resource.Id.switchHold);
        _seekHold = FindViewById<SeekBar>(Resource.Id.seekHoldSeconds);
        _valueLabel = FindViewById<TextView>(Resource.Id.txtHoldValue);

        // Configure SeekBar: range 0–10 (internally maps to 5–15 seconds)
        if (_seekHold != null)
        {
            _seekHold.Max = 10;

            // Event: user drags slider to change hold duration
            _seekHold.ProgressChanged += (s, e) =>
            {
                if (!e.FromUser) return; // ignore programmatic changes
                UpdateSecondsLabel(e.Progress); // update text label
                SaveOffset(e.Progress);         // save new value
            };
        }

        // Configure Switch: enables or disables the "hold" functionality
        if (_switchHold != null)
        {
            _switchHold.CheckedChange += (s, e) =>
            {
                var sp = GetSharedPreferences(PREFS, FileCreationMode.Private);
                using var edit = sp.Edit();
                edit.PutBoolean(PREF_HOLD_ENABLED, e.IsChecked);
                edit.Commit(); // commit immediately (blocking)
            };
        }
    }

    // ================================================================
    // LIFECYCLE: OnResume
    // ================================================================
    /// <summary>
    /// Reloads saved preferences every time the activity becomes visible.
    /// Ensures UI reflects current settings.
    /// </summary>
    protected override void OnResume()
    {
        base.OnResume();

        // Access stored preferences
        var sp = GetSharedPreferences(PREFS, FileCreationMode.Private);

        // Load stored values (or defaults)
        bool enabled = sp.GetBoolean(PREF_HOLD_ENABLED, true);
        int offset = sp.GetInt(PREF_HOLD_SECONDS, 5); // default = midpoint (10s)

        // Clamp offset for safety (0..10)
        if (offset < 0) offset = 0;
        if (offset > 10) offset = 10;

        // Update UI with loaded settings
        if (_switchHold != null) _switchHold.Checked = enabled;
        if (_seekHold != null)
        {
            _seekHold.Progress = offset;
            UpdateSecondsLabel(offset);
        }
    }

    // ================================================================
    // HELPER METHODS
    // ================================================================
    /// <summary>
    /// Converts a 0–10 slider offset into an actual number of seconds (5–15)
    /// and updates the text label accordingly.
    /// </summary>
    private void UpdateSecondsLabel(int offset)
    {
        int seconds = 5 + offset; // offset 0 → 5s, offset 10 → 15s
        if (_valueLabel != null)
            _valueLabel.Text = $"{seconds} seconds";
    }

    /// <summary>
    /// Saves the current SeekBar offset (0–10) to SharedPreferences.
    /// </summary>
    private void SaveOffset(int offset)
    {
        // Clamp to valid range for safety
        if (offset < 0) offset = 0;
        if (offset > 10) offset = 10;

        var sp = GetSharedPreferences(PREFS, FileCreationMode.Private);
        using var edit = sp.Edit();
        edit.PutInt(PREF_HOLD_SECONDS, offset);
        edit.Commit(); // commit immediately (ensures persistence)
    }
}