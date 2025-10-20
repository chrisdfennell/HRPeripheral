using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace HRPeripheral;

/// <summary>
/// Settings screen: manages hold-to-exit options and Bluetooth “forget devices”.
/// </summary>
[Activity(Label = "Settings", Theme = "@android:style/Theme.DeviceDefault")]
public class SettingsActivity : Activity
{
    // ================================================================
    // CONSTANTS
    // ================================================================
    private const string PREFS = "hrp_prefs";
    private const string PREF_HOLD_ENABLED = "hold_enabled";
    private const string PREF_HOLD_SECONDS = "hold_seconds"; // 0..10 -> 5..15 seconds

    // ================================================================
    // UI
    // ================================================================
    private Switch? _switchHold;
    private SeekBar? _seekHold;
    private TextView? _valueLabel;

    private Button? _btnForgetAll;
    private LinearLayout? _knownContainer;

    // ================================================================
    // LIFECYCLE
    // ================================================================
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.settings_activity);

        // hold-to-exit
        _switchHold = FindViewById<Switch>(Resource.Id.switchHold);
        _seekHold = FindViewById<SeekBar>(Resource.Id.seekHoldSeconds);
        _valueLabel = FindViewById<TextView>(Resource.Id.txtHoldValue);

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

        // bluetooth: forget all + list
        _btnForgetAll = FindViewById<Button>(Resource.Id.btn_forget_all);
        _knownContainer = FindViewById<LinearLayout>(Resource.Id.container_known_devices);

        if (_btnForgetAll != null)
        {
            _btnForgetAll.Click += (_, __) =>
            {
                var ble = BleHost.Peripheral;
                if (ble == null)
                {
                    Toast.MakeText(this, "BLE not available", ToastLength.Short).Show();
                    return;
                }

                // stop → forget → start
                ble.StopAdvertising();
                ble.ForgetAllDevices(alsoUnbond: true); // set false to avoid unpairing via reflection
                ble.StartAdvertising();

                Toast.MakeText(this, "All devices forgotten", ToastLength.Short).Show();
                RebuildKnownDevices();
            };
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        // prefs -> ui
        var sp = GetSharedPreferences(PREFS, FileCreationMode.Private);
        bool enabled = sp.GetBoolean(PREF_HOLD_ENABLED, true);
        int offset = sp.GetInt(PREF_HOLD_SECONDS, 5);
        if (offset < 0) offset = 0;
        if (offset > 10) offset = 10;

        if (_switchHold != null) _switchHold.Checked = enabled;
        if (_seekHold != null)
        {
            _seekHold.Progress = offset;
            UpdateSecondsLabel(offset);
        }

        // refresh BLE list every time
        RebuildKnownDevices();
    }

    // ================================================================
    // HELPERS
    // ================================================================
    private void UpdateSecondsLabel(int offset)
    {
        int seconds = 5 + offset;
        if (_valueLabel != null)
            _valueLabel.Text = $"{seconds} seconds";
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

    private void RebuildKnownDevices()
    {
        if (_knownContainer == null)
            return;

        _knownContainer.RemoveAllViews();

        var ble = BleHost.Peripheral;
        if (ble == null)
        {
            _knownContainer.AddView(new TextView(this) { Text = "BLE not available" });
            return;
        }

        var list = ble.KnownDevices;
        if (list.Count == 0)
        {
            _knownContainer.AddView(new TextView(this) { Text = "No known devices" });
            return;
        }

        foreach (var addr in list)
        {
            // Row = [ address | Forget ]
            var row = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal
            };

            var lpText = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            var tv = new TextView(this) { Text = addr };
            tv.LayoutParameters = lpText;

            var btn = new Button(this) { Text = "Forget" };
            btn.Click += (_, __) =>
            {
                // forget a single device
                ble.ForgetDevice(addr, alsoUnbond: true);
                Toast.MakeText(this, $"Forgot {addr}", ToastLength.Short).Show();
                RebuildKnownDevices();
            };

            row.AddView(tv);
            row.AddView(btn);
            _knownContainer.AddView(row);
        }
    }
}