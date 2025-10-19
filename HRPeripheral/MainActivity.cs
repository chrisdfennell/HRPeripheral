using Android.App;
using Android.OS;
using Android.Widget;
using Android.Content.PM;
using Android.Util;
using System.Timers;
using Timer = System.Timers.Timer;

namespace HRPeripheral;

[Activity(
    Label = "HR Peripheral",
    MainLauncher = true,
    Theme = "@android:style/Theme.DeviceDefault",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize
)]
public class MainActivity : Activity
{
    const string TAG = "HRPeripheral";

    // Peripheral (advertises HR service)
    private BlePeripheral? _peripheral;

    // Simple heartbeat generator to prove it works with the bike
    private Timer? _tick;
    private byte _bpm = 75; // start value

    private TextView? _hrText;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        _hrText = FindViewById<TextView>(Resource.Id.hr_value);

        if (!BlePermissions.HasAll(this))
        {
            BlePermissions.Request(this);
        }
        else
        {
            InitPeripheral();
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == BlePermissions.RequestCode && BlePermissions.HasAll(this))
            InitPeripheral();
        else
            Toast.MakeText(this, "Bluetooth permissions required.", ToastLength.Long).Show();
    }

    void InitPeripheral()
    {
        if (_peripheral != null) return;
        _peripheral = new BlePeripheral(this);
    }

    protected override void OnResume()
    {
        base.OnResume();

        try
        {
            if (!BlePermissions.HasAll(this))
            {
                Toast.MakeText(this, "Grant Bluetooth permissions.", ToastLength.Short).Show();
                return;
            }

            // Start advertising/GATT
            var ok = _peripheral?.StartAsync() ?? false;
            if (!ok)
            {
                Toast.MakeText(this, "BLE advertising not supported or Bluetooth off.", ToastLength.Long).Show();
                return;
            }

            Toast.MakeText(this, "Advertising Heart Rate Service…", ToastLength.Short).Show();

            // Kick off a 1 Hz “fake HR” so the bike can read something immediately
            _tick = new Timer(1000);
            _tick.Elapsed += (_, __) =>
            {
                // bounce BPM 75..145 to make it obvious
                _bpm = (byte)(_bpm >= 145 ? 75 : _bpm + 1);

                try { _peripheral?.NotifyHeartRate(_bpm); } catch { /* ignore */ }

                RunOnUiThread(() =>
                {
                    try { _hrText?.SetText($"{_bpm} bpm", TextView.BufferType.Normal); } catch { }
                });
            };
            _tick.AutoReset = true;
            _tick.Start();
        }
        catch (System.Exception ex)
        {
            Log.Error(TAG, "OnResume error: " + ex);
            Toast.MakeText(this, "Error: " + ex.Message, ToastLength.Long).Show();
        }
    }

    protected override void OnPause()
    {
        try { _tick?.Stop(); _tick?.Dispose(); _tick = null; } catch { }
        try { _peripheral?.Stop(); } catch { }
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        try { _tick?.Stop(); _tick?.Dispose(); _tick = null; } catch { }
        try { _peripheral?.Stop(); } catch { }
        base.OnDestroy();
    }
}