using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace HRPeripheral;

/// <summary>
/// Settings screen: manages hold-to-exit options and Bluetooth “forget devices”.
/// </summary>
[Activity(
    Label = "Settings",
    Theme = "@android:style/Theme.DeviceDefault",
    ScreenOrientation = ScreenOrientation.Portrait   // <-- Ensure this line is present
)]
public class SettingsActivity : Activity
{
    // SharedPreferences — keys and defaults centralized in HrpPrefs

    // ================================================================
    // UI
    // ================================================================
    private Switch? _switchHold;
    private SeekBar? _seekHold;
    private TextView? _valueLabel;

    private Switch? _switchAutoPause;

    private Switch? _switchCalMale;
    private SeekBar? _seekWeight;
    private SeekBar? _seekAge;
    private TextView? _txtWeightValue;
    private TextView? _txtAgeValue;

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
                var sp = GetSharedPreferences(HrpPrefs.PREFS_NAME, FileCreationMode.Private)!;
                using var edit = sp.Edit()!;
                edit.PutBoolean(HrpPrefs.KEY_HOLD_ENABLED, e.IsChecked);
                edit.Commit();
            };
        }

        // auto-pause
        _switchAutoPause = FindViewById<Switch>(Resource.Id.switchAutoPause);
        if (_switchAutoPause != null)
        {
            _switchAutoPause.CheckedChange += (s, e) =>
            {
                var sp = GetSharedPreferences(HrpPrefs.PREFS_NAME, FileCreationMode.Private)!;
                using var edit = sp.Edit()!;
                edit.PutBoolean(HrpPrefs.KEY_AUTO_PAUSE, e.IsChecked);
                edit.Commit();
            };
        }

        // calorie profile
        _switchCalMale = FindViewById<Switch>(Resource.Id.switchCalMale);
        _seekWeight = FindViewById<SeekBar>(Resource.Id.seekWeight);
        _seekAge = FindViewById<SeekBar>(Resource.Id.seekAge);
        _txtWeightValue = FindViewById<TextView>(Resource.Id.txtWeightValue);
        _txtAgeValue = FindViewById<TextView>(Resource.Id.txtAgeValue);

        if (_switchCalMale != null)
        {
            _switchCalMale.CheckedChange += (s, e) =>
            {
                var sp = GetSharedPreferences(HrpPrefs.PREFS_NAME, FileCreationMode.Private)!;
                using var edit = sp.Edit()!;
                edit.PutBoolean(HrpPrefs.KEY_CAL_MALE, e.IsChecked);
                edit.Commit();
            };
        }

        if (_seekWeight != null)
        {
            _seekWeight.Max = HrpPrefs.MAX_CAL_WEIGHT_KG - HrpPrefs.MIN_CAL_WEIGHT_KG;
            _seekWeight.ProgressChanged += (s, e) =>
            {
                if (!e.FromUser) return;
                int kg = e.Progress + HrpPrefs.MIN_CAL_WEIGHT_KG;
                if (_txtWeightValue != null) _txtWeightValue.Text = $"{kg} kg";
                var sp = GetSharedPreferences(HrpPrefs.PREFS_NAME, FileCreationMode.Private)!;
                using var edit = sp.Edit()!;
                edit.PutInt(HrpPrefs.KEY_CAL_WEIGHT_KG, kg);
                edit.Commit();
            };
        }

        if (_seekAge != null)
        {
            _seekAge.Max = HrpPrefs.MAX_CAL_AGE - HrpPrefs.MIN_CAL_AGE;
            _seekAge.ProgressChanged += (s, e) =>
            {
                if (!e.FromUser) return;
                int age = e.Progress + HrpPrefs.MIN_CAL_AGE;
                if (_txtAgeValue != null) _txtAgeValue.Text = $"{age}";
                var sp = GetSharedPreferences(HrpPrefs.PREFS_NAME, FileCreationMode.Private)!;
                using var edit = sp.Edit()!;
                edit.PutInt(HrpPrefs.KEY_CAL_AGE, age);
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
                    Toast.MakeText(this, "BLE not available", ToastLength.Short)!.Show();
                    return;
                }

                // stop → forget → start
                ble.StopAdvertising();
                ble.ForgetAllDevices(alsoUnbond: true); // set false to avoid unpairing via reflection
                ble.StartAdvertising();

                Toast.MakeText(this, "All devices forgotten", ToastLength.Short)!.Show();
                RebuildKnownDevices();
            };
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        // prefs -> ui
        var sp = GetSharedPreferences(HrpPrefs.PREFS_NAME, FileCreationMode.Private)!;
        bool enabled = sp.GetBoolean(HrpPrefs.KEY_HOLD_ENABLED, HrpPrefs.DEFAULT_HOLD_ENABLED);
        int offset = HrpPrefs.ClampHoldOffset(sp.GetInt(HrpPrefs.KEY_HOLD_SECONDS, HrpPrefs.DEFAULT_HOLD_OFFSET));

        if (_switchHold != null) _switchHold.Checked = enabled;
        if (_seekHold != null)
        {
            _seekHold.Progress = offset;
            UpdateSecondsLabel(offset);
        }

        // auto-pause
        bool autoPause = sp.GetBoolean(HrpPrefs.KEY_AUTO_PAUSE, HrpPrefs.DEFAULT_AUTO_PAUSE);
        if (_switchAutoPause != null) _switchAutoPause.Checked = autoPause;

        // calorie profile
        bool calMale = sp.GetBoolean(HrpPrefs.KEY_CAL_MALE, HrpPrefs.DEFAULT_CAL_MALE);
        int calWeight = HrpPrefs.ClampWeight(sp.GetInt(HrpPrefs.KEY_CAL_WEIGHT_KG, HrpPrefs.DEFAULT_CAL_WEIGHT_KG));
        int calAge = HrpPrefs.ClampAge(sp.GetInt(HrpPrefs.KEY_CAL_AGE, HrpPrefs.DEFAULT_CAL_AGE));

        if (_switchCalMale != null) _switchCalMale.Checked = calMale;
        if (_seekWeight != null)
        {
            _seekWeight.Progress = calWeight - HrpPrefs.MIN_CAL_WEIGHT_KG;
            if (_txtWeightValue != null) _txtWeightValue.Text = $"{calWeight} kg";
        }
        if (_seekAge != null)
        {
            _seekAge.Progress = calAge - HrpPrefs.MIN_CAL_AGE;
            if (_txtAgeValue != null) _txtAgeValue.Text = $"{calAge}";
        }

        // refresh BLE list every time
        RebuildKnownDevices();
    }

    // ================================================================
    // HELPERS
    // ================================================================
    private void UpdateSecondsLabel(int offset)
    {
        int seconds = HrpPrefs.BASE_HOLD_SECONDS + offset;
        if (_valueLabel != null)
            _valueLabel.Text = $"{seconds} seconds";
    }

    private void SaveOffset(int offset)
    {
        offset = HrpPrefs.ClampHoldOffset(offset);
        var sp = GetSharedPreferences(HrpPrefs.PREFS_NAME, FileCreationMode.Private)!;
        using var edit = sp.Edit()!;
        edit.PutInt(HrpPrefs.KEY_HOLD_SECONDS, offset);
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
                Toast.MakeText(this, $"Forgot {addr}", ToastLength.Short)!.Show();
                RebuildKnownDevices();
            };

            row.AddView(tv);
            row.AddView(btn);
            _knownContainer.AddView(row);
        }
    }
}