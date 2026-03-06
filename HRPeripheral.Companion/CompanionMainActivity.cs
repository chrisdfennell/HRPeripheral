using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Debug = System.Diagnostics.Debug;
using Activity = Android.App.Activity;

namespace HRPeripheral.Companion;

[Activity(
    Label = "HR Companion",
    MainLauncher = true,
    Theme = "@android:style/Theme.DeviceDefault",
    Exported = true,
    ScreenOrientation = ScreenOrientation.Portrait
)]
public class CompanionMainActivity : Activity
{
    // UI elements
    private TextView? _txtConnection;
    private TextView? _txtHrValue;
    private TextView? _txtZoneName;
    private TextView? _txtBattery;
    private CompanionHrGraphView? _hrGraph;
    private TextView? _txtCalories;
    private TextView? _txtDuration;
    private LinearLayout? _zoneBreakdown;
    private Button? _btnScan;

    // Zone breakdown bar views
    private readonly TextView[] _zoneLabels = new TextView[5];
    private readonly View[] _zoneBars = new View[5];

    // BLE
    private BleScanner? _scanner;
    private bool _scanning;
    private readonly List<BluetoothDevice> _foundDevices = new();

    // Session tracking
    private SessionTracker? _session;
    private int _age = HrpPrefs.DEFAULT_CAL_AGE;
    private int _batteryLevel = -1;

    // Broadcast receivers
    private HrUpdateReceiver? _hrReceiver;
    private ConnectionReceiver? _connReceiver;

    // Duration timer
    private readonly Handler _timerHandler = new(Looper.MainLooper!);
    private Java.Lang.IRunnable? _timerTick;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_companion_main);

        _txtConnection = FindViewById<TextView>(Resource.Id.txtConnection);
        _txtHrValue = FindViewById<TextView>(Resource.Id.txtHrValue);
        _txtZoneName = FindViewById<TextView>(Resource.Id.txtZoneName);
        _txtBattery = FindViewById<TextView>(Resource.Id.txtBattery);
        _hrGraph = FindViewById<CompanionHrGraphView>(Resource.Id.hrGraph);
        _txtCalories = FindViewById<TextView>(Resource.Id.txtCalories);
        _txtDuration = FindViewById<TextView>(Resource.Id.txtDuration);
        _zoneBreakdown = FindViewById<LinearLayout>(Resource.Id.zoneBreakdown);
        _btnScan = FindViewById<Button>(Resource.Id.btnScan);

        // Load user profile from shared prefs (or use defaults)
        LoadProfile();

        // Build zone breakdown UI
        BuildZoneBreakdown();

        // Scan button
        if (_btnScan != null)
        {
            _btnScan.Click += (s, e) =>
            {
                if (_scanning)
                {
                    _scanner?.StopScan();
                    return;
                }

                if (!CompanionBlePermissions.HasAll(this))
                {
                    CompanionBlePermissions.Request(this);
                    return;
                }

                StartScanning();
            };
        }

        // Duration timer
        _timerTick = new Java.Lang.Runnable(() =>
        {
            UpdateDuration();
            _timerHandler.PostDelayed(_timerTick, 1000);
        });

        if (!CompanionBlePermissions.HasAll(this))
            CompanionBlePermissions.Request(this);
    }

    public override bool OnCreateOptionsMenu(IMenu? menu)
    {
        menu?.Add(0, 1, 0, "Settings");
        return true;
    }

    public override bool OnOptionsItemSelected(IMenuItem item)
    {
        if (item.ItemId == 1)
        {
            StartActivity(new Intent(this, typeof(CompanionSettingsActivity)));
            return true;
        }
        return base.OnOptionsItemSelected(item);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == CompanionBlePermissions.RequestCode && !CompanionBlePermissions.HasAll(this))
        {
            Toast.MakeText(this, "BLE permissions required.", ToastLength.Long)?.Show();
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Reload profile in case settings changed
        LoadProfile();

        _hrReceiver = new HrUpdateReceiver(this);
        _connReceiver = new ConnectionReceiver(this);

        var hrFilter = new IntentFilter(BleCentralService.ACTION_HR_UPDATE);
        var connFilter = new IntentFilter(BleCentralService.ACTION_CONNECTION);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            RegisterReceiver(_hrReceiver, hrFilter, ReceiverFlags.NotExported);
            RegisterReceiver(_connReceiver, connFilter, ReceiverFlags.NotExported);
        }
        else
        {
            RegisterReceiver(_hrReceiver, hrFilter);
            RegisterReceiver(_connReceiver, connFilter);
        }

        _timerHandler.Post(_timerTick);
    }

    protected override void OnPause()
    {
        _timerHandler.RemoveCallbacksAndMessages(null);
        try { if (_hrReceiver != null) UnregisterReceiver(_hrReceiver); } catch (Exception ex) { Debug.WriteLine($"Unregister HR receiver error: {ex.Message}"); }
        try { if (_connReceiver != null) UnregisterReceiver(_connReceiver); } catch (Exception ex) { Debug.WriteLine($"Unregister connection receiver error: {ex.Message}"); }
        base.OnPause();
    }

    // =====================================================================
    // PROFILE
    // =====================================================================

    private void LoadProfile()
    {
        var sp = GetSharedPreferences(HrpPrefs.PREFS_NAME, FileCreationMode.Private);
        bool male = sp.GetBoolean(HrpPrefs.KEY_CAL_MALE, HrpPrefs.DEFAULT_CAL_MALE);
        int weight = HrpPrefs.ClampWeight(sp.GetInt(HrpPrefs.KEY_CAL_WEIGHT_KG, HrpPrefs.DEFAULT_CAL_WEIGHT_KG));
        _age = HrpPrefs.ClampAge(sp.GetInt(HrpPrefs.KEY_CAL_AGE, HrpPrefs.DEFAULT_CAL_AGE));
        var cal = new CalorieEstimator(male, weight, _age);
        _session = new SessionTracker(cal, _age);
        _hrGraph?.SetAge(_age);
    }

    // =====================================================================
    // SCANNING + DEVICE PICKER
    // =====================================================================

    private void StartScanning()
    {
        var btManager = (BluetoothManager?)GetSystemService(BluetoothService);
        var adapter = btManager?.Adapter;
        if (adapter == null || !adapter.IsEnabled)
        {
            Toast.MakeText(this, "Bluetooth is off.", ToastLength.Short)?.Show();
            return;
        }

        _scanning = true;
        _foundDevices.Clear();
        _btnScan!.Text = "Scanning...";
        _txtConnection!.Text = "Scanning for HR monitors...";

        _scanner = new BleScanner();
        _scanner.OnDeviceFound += OnDeviceFound;
        _scanner.OnScanStopped += () => RunOnUiThread(() =>
        {
            _scanning = false;
            _btnScan!.Text = "Scan for HR Monitor";
            if (_foundDevices.Count == 0)
            {
                _txtConnection!.Text = "No HR monitors found.";
            }
            else if (_foundDevices.Count == 1)
            {
                // Only one device found — connect directly
                ConnectToDevice(_foundDevices[0]);
            }
            else
            {
                // Multiple devices — show picker
                ShowDevicePicker();
            }
        });

        _scanner.StartScan(adapter);
    }

    private void OnDeviceFound(BluetoothDevice device)
    {
        RunOnUiThread(() =>
        {
            _foundDevices.Add(device);
            _txtConnection!.Text = $"Found {_foundDevices.Count} device(s)...";
        });
    }

    private void ShowDevicePicker()
    {
        var names = _foundDevices
            .Select(d => string.IsNullOrEmpty(d.Name) ? d.Address! : $"{d.Name} ({d.Address})")
            .ToArray();

        new AlertDialog.Builder(this)
            .SetTitle("Select HR Monitor")!
            .SetItems(names, (sender, args) =>
            {
                ConnectToDevice(_foundDevices[args.Which]);
            })!
            .SetNegativeButton("Cancel", (EventHandler<DialogClickEventArgs>?)null)!
            .Show();
    }

    private void ConnectToDevice(BluetoothDevice device)
    {
        _txtConnection!.Text = $"Connecting to {device.Name ?? device.Address}...";

        var intent = new Intent(this, typeof(BleCentralService));
        intent.PutExtra(BleCentralService.EXTRA_DEVICE_ADDRESS, device.Address);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            StartForegroundService(intent);
        else
            StartService(intent);

        // Reset session
        _session?.Reset();
        _hrGraph?.Clear();
    }

    // =====================================================================
    // HR / BATTERY UPDATES
    // =====================================================================

    private void OnHrUpdate(int bpm)
    {
        _txtHrValue!.Text = bpm.ToString();

        // Record in session tracker (updates stats, calories, zone time)
        _session?.RecordHr(bpm, DateTime.UtcNow);

        // Zone
        int zone = HeartRateZone.GetZone(bpm, _age);
        if (zone > 0)
        {
            var z = HeartRateZone.Zones[zone - 1];
            var (r, g, b) = HeartRateZone.GetZoneColor(zone);
            _txtZoneName!.Text = $"Z{zone} {z.Name}";
            _txtZoneName.SetTextColor(Color.Rgb(r, g, b));
        }
        else
        {
            _txtZoneName!.Text = "";
        }

        // Graph
        _hrGraph?.Push(bpm);

        // Calories
        if (_session != null)
            _txtCalories!.Text = $"{_session.TotalKcal:F1} kcal";

        UpdateZoneBreakdown();
    }

    private void OnBatteryUpdate(int level)
    {
        _batteryLevel = level;
        _txtBattery!.Text = $"Watch: {level}%";
    }

    private void OnConnectionChanged(bool connected)
    {
        _txtConnection!.Text = connected ? "Connected" : "Disconnected";
        _txtConnection.SetTextColor(connected
            ? Color.Argb(255, 0x66, 0xBB, 0x6A)
            : Color.Argb(255, 0xAA, 0xAA, 0xAA));
    }

    // =====================================================================
    // ZONE BREAKDOWN UI
    // =====================================================================

    private void BuildZoneBreakdown()
    {
        if (_zoneBreakdown == null) return;
        _zoneBreakdown.RemoveAllViews();

        for (int i = 0; i < 5; i++)
        {
            var row = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal
            };
            row.SetPadding(0, 4, 0, 4);

            var (r, g, b) = HeartRateZone.GetZoneColor(i + 1);

            var label = new TextView(this)
            {
                Text = $"Z{i + 1}",
                TextSize = 14f,
            };
            label.SetTextColor(Color.Rgb(r, g, b));
            label.SetWidth(50);
            _zoneLabels[i] = label;

            var bar = new View(this);
            bar.SetBackgroundColor(Color.Rgb(r, g, b));
            var barParams = new LinearLayout.LayoutParams(0, 20) { Weight = 0 };
            barParams.SetMargins(8, 0, 8, 0);
            bar.LayoutParameters = barParams;
            _zoneBars[i] = bar;

            row.AddView(label);
            row.AddView(bar);
            _zoneBreakdown.AddView(row);
        }
    }

    private void UpdateZoneBreakdown()
    {
        var times = _session?.GetZoneTimes() ?? new TimeSpan[5];
        double maxSec = 1; // avoid /0
        for (int i = 0; i < 5; i++)
        {
            if (times[i].TotalSeconds > maxSec)
                maxSec = times[i].TotalSeconds;
        }

        for (int i = 0; i < 5; i++)
        {
            float weight = (float)(times[i].TotalSeconds / maxSec);
            if (weight < 0.01f && times[i].TotalSeconds > 0) weight = 0.01f;

            var lp = _zoneBars[i].LayoutParameters as LinearLayout.LayoutParams;
            if (lp != null)
            {
                lp.Weight = weight;
                _zoneBars[i].LayoutParameters = lp;
            }

            string timeStr = times[i].TotalSeconds < 60
                ? $"{times[i].TotalSeconds:F0}s"
                : $"{(int)times[i].TotalMinutes}:{times[i].Seconds:D2}";
            _zoneLabels[i].Text = $"Z{i + 1} {timeStr}";
        }
    }

    private void UpdateDuration()
    {
        if (_session == null) return;
        _txtDuration!.Text = SessionTracker.FormatDuration(_session.Elapsed(DateTime.UtcNow));
    }

    // =====================================================================
    // BROADCAST RECEIVERS
    // =====================================================================

    private class HrUpdateReceiver : BroadcastReceiver
    {
        private readonly CompanionMainActivity _host;
        public HrUpdateReceiver(CompanionMainActivity host) => _host = host;

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != BleCentralService.ACTION_HR_UPDATE) return;

            int hr = intent.GetIntExtra("hr", 0);
            if (hr > 0)
                _host.RunOnUiThread(() => _host.OnHrUpdate(hr));

            int batt = intent.GetIntExtra("battery", -1);
            if (batt >= 0)
                _host.RunOnUiThread(() => _host.OnBatteryUpdate(batt));
        }
    }

    private class ConnectionReceiver : BroadcastReceiver
    {
        private readonly CompanionMainActivity _host;
        public ConnectionReceiver(CompanionMainActivity host) => _host = host;

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != BleCentralService.ACTION_CONNECTION) return;
            bool connected = intent.GetBooleanExtra("connected", false);
            _host.RunOnUiThread(() => _host.OnConnectionChanged(connected));
        }
    }
}
