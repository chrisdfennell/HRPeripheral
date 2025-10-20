using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware;
using Android.OS;
using System;
// Explicit aliases to avoid ambiguity and to log safely
using SysDebug = System.Diagnostics.Debug;
using SysException = System.Exception;

namespace HRPeripheral;

[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeHealth | ForegroundService.TypeConnectedDevice
)]
public class HeartRateService : Service, ISensorEventListener
{
    public const string CHANNEL_ID = "hr_channel";
    public static readonly string ACTION_UPDATE = "com.fennell.hrperipheral.UPDATE";
    public const string ACTION_FORCE_CLOSE = "hrperipheral.action.FORCE_CLOSE";

    private SensorManager? _sm;
    private Sensor? _hrSensor;
    private BlePeripheral? _ble;
    private int _currentHr;
    private DateTime _start;
    private double _kcal;
    private CalorieEstimator? _cal;
    private BroadcastReceiver? _forceCloseReceiver;

    public override void OnCreate()
    {
        base.OnCreate();

        _start = DateTime.UtcNow;
        _cal = CalorieEstimator.DefaultMale75kgAge35();

        CreateChannel();

        var notif = new Notification.Builder(this, CHANNEL_ID)
            .SetContentTitle("Heart Rate Broadcasting")
            .SetContentText("Advertising Heart Rate Service")
            .SetSmallIcon(Android.Resource.Drawable.StatSysDataBluetooth)
            .SetOngoing(true)
            .Build();

        StartForeground(1, notif);

        _sm = (SensorManager)GetSystemService(SensorService);
        _hrSensor = _sm?.GetDefaultSensor(SensorType.HeartRate);
        if (_hrSensor != null)
        {
            _sm!.RegisterListener(this, _hrSensor, SensorDelay.Normal);
        }

        _ble = new BlePeripheral(this);
        _ble.StartAdvertising();

        // Optional broadcast-driven shutdown
        _forceCloseReceiver = new ForceCloseReceiver(this);
        var forceFilter = new IntentFilter(ACTION_FORCE_CLOSE);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            RegisterReceiver(_forceCloseReceiver, forceFilter, ReceiverFlags.NotExported);
        }
        else
        {
            RegisterReceiver(_forceCloseReceiver, forceFilter);
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        try
        {
            if (_forceCloseReceiver != null)
                UnregisterReceiver(_forceCloseReceiver);
        }
        catch (SysException) { /* ignore */ }

        try { _sm?.UnregisterListener(this); } catch (SysException) { }
        try { _ble?.StopAdvertising(); } catch (SysException) { }

        StopForeground(true);
        base.OnDestroy();
    }

    // === ISensorEventListener ===
    public void OnAccuracyChanged(Sensor? sensor, SensorStatus accuracy) { }

    public void OnSensorChanged(SensorEvent? e)
    {
        if (e?.Sensor?.Type != SensorType.HeartRate) return;

        // IList<float> => use Count, not Length
        var hr = (e.Values != null && e.Values.Count > 0)
            ? (int)Math.Round(e.Values[0])
            : 0;

        if (hr <= 0 || hr > 230) return;

        _currentHr = hr;

        if (_cal != null)
        {
            var kcalPerMin = _cal.KcalPerMinute(hr);
            _kcal += kcalPerMin / 60.0; // integrate assuming ~1s updates
        }

        _ble?.UpdateHeartRate(hr);
        BroadcastUpdate();
    }

    private void BroadcastUpdate()
    {
        var intent = new Intent(ACTION_UPDATE);
        intent.PutExtra("type", 1);
        intent.PutExtra("hr", _currentHr);
        intent.PutExtra("kcal", _kcal);
        intent.SetPackage(PackageName);
        SendBroadcast(intent);
    }

    private void CreateChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(CHANNEL_ID, "Heart Rate", NotificationImportance.Low);
            var mgr = (NotificationManager?)GetSystemService(NotificationService);
            mgr?.CreateNotificationChannel(channel);
        }
    }

    private sealed class ForceCloseReceiver : BroadcastReceiver
    {
        private readonly HeartRateService _svc;
        public ForceCloseReceiver(HeartRateService svc) => _svc = svc;

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != ACTION_FORCE_CLOSE) return;

            try { _svc._sm?.UnregisterListener(_svc); } catch (SysException) { }
            try { _svc._ble?.StopAdvertising(); } catch (SysException) { }
            try { _svc.StopForeground(true); } catch (SysException) { }
            try { _svc.StopSelf(); } catch (SysException ex) { SysDebug.WriteLine($"ForceClose StopSelf error: {ex}"); }
        }
    }
}