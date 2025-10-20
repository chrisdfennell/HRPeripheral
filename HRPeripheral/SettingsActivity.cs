using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace HRPeripheral;

[Activity(Label = "Settings", Theme = "@android:style/Theme.DeviceDefault")]
public class SettingsActivity : Activity
{
    private const string PREFS = "hrp_prefs";
    private const string PREF_HOLD_ENABLED = "hold_enabled";
    private const string PREF_HOLD_SECONDS = "hold_seconds"; // stores offset 0..10 -> 5..15s

    private Switch? _switchHold;
    private SeekBar? _seekHold;
    private TextView? _valueLabel;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.settings_activity);

        _switchHold = FindViewById<Switch>(Resource.Id.switchHold);
        _seekHold = FindViewById<SeekBar>(Resource.Id.seekHoldSeconds);
        _valueLabel = FindViewById<TextView>(Resource.Id.txtHoldValue);

        // SeekBar 0..10 (maps to 5..15 seconds)
        if (_seekHold != null)
        {
            _seekHold.Max = 10;
            _seekHold.ProgressChanged += (s, e) =>
            {
                if (!e.FromUser) return;
                UpdateSecondsLabel(e.Progress);
                SaveOffset(e.Progress);
            };
        }

        if (_switchHold != null)
        {
            _switchHold.CheckedChange += (s, e) =>
            {
                var sp = GetSharedPreferences(PREFS, FileCreationMode.Private);
                using var edit = sp.Edit();
                edit.PutBoolean(PREF_HOLD_ENABLED, e.IsChecked);
                edit.Commit();
            };
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        // Load prefs
        var sp = GetSharedPreferences(PREFS, FileCreationMode.Private);
        bool enabled = sp.GetBoolean(PREF_HOLD_ENABLED, true);
        int offset = sp.GetInt(PREF_HOLD_SECONDS, 5); // default 10s
        if (offset < 0) offset = 0;
        if (offset > 10) offset = 10;

        if (_switchHold != null) _switchHold.Checked = enabled;
        if (_seekHold != null) { _seekHold.Progress = offset; UpdateSecondsLabel(offset); }
    }

    private void UpdateSecondsLabel(int offset)
    {
        int seconds = 5 + offset;
        if (_valueLabel != null) _valueLabel.Text = $"{seconds} seconds";
    }

    private void SaveOffset(int offset)
    {
        if (offset < 0) offset = 0;
        if (offset > 10) offset = 10;

        var sp = GetSharedPreferences(PREFS, FileCreationMode.Private);
        using var edit = sp.Edit();
        edit.PutInt(PREF_HOLD_SECONDS, offset);
        edit.Commit();
    }
}