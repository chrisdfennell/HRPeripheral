using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware;
using Android.OS;
using Android.Runtime;
using System;
using System.Collections.Generic;

namespace HRPeripheral;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeHealth | ForegroundService.TypeConnectedDevice)]
public class HeartRateService : Service, ISensorEventListener
{
    public const string CHANNEL_ID = "hr_channel";
    public static readonly string ACTION_UPDATE = "com.fennell.hrperipheral.UPDATE";

    SensorManager _sm;
    Sensor _hrSensor;
    BlePeripheral _ble;
    int _currentHr;
    DateTime _start;
    double _kcal;
    CalorieEstimator _cal;

    public override void OnCreate()
    {
        base.OnCreate();
        _start = DateTime.UtcNow;
        _cal = CalorieEstimator.DefaultMale75kgAge35(); // customize later

        CreateChannel();
        var notif = new Notification.Builder(this, CHANNEL_ID)
            .SetContentTitle("Heart Rate Broadcasting")
            .SetContentText("Advertising Heart Rate Service")
            .SetSmallIcon(Android.Resource.Drawable.StatSysDataBluetooth)
            .SetOngoing(true)
            .Build();
        StartForeground(1, notif);

        _sm = (SensorManager)GetSystemService(SensorService);
        _hrSensor = _sm.GetDefaultSensor(SensorType.HeartRate);
        _sm.RegisterListener(this, _hrSensor, SensorDelay.Normal);

        _ble = new BlePeripheral(this);
        _ble.StartAsync(); // start GATT server + advertising
    }

    void BroadcastUpdate()
    {
        var intent = new Intent(ACTION_UPDATE);
        intent.PutExtra("type", 1);
        intent.PutExtra("hr", _currentHr);
        intent.PutExtra("kcal", _kcal);
        SendBroadcast(intent);
    }

    public override IBinder OnBind(Intent intent) => null!;

    public override void OnDestroy()
    {
        base.OnDestroy();
        _sm.UnregisterListener(this);
        _ble?.Stop();
        StopForeground(true);
    }

    // ISensorEventListener
    public void OnAccuracyChanged(Sensor sensor, SensorStatus accuracy) { }

    public void OnSensorChanged(SensorEvent e)
    {
        if (e?.Sensor?.Type != SensorType.HeartRate) return;
        var hr = (int)Math.Round(e.Values?[0] ?? 0);
        if (hr <= 0 || hr > 230) return;

        _currentHr = hr;

        // Update calories (rough estimate, per second interval)
        var now = DateTime.UtcNow;
        var dt = now - _start; // total elapsed
        // estimate kcal by integrating kcal/min over small slices:
        var kcalPerMin = _cal.KcalPerMinute(hr);
        // approximate: assume callback ~ once per second (safe accumulation)
        _kcal += kcalPerMin / 60.0;

        // Notify BLE subscribers
        _ble?.NotifyHeartRate((byte)hr);

        // Update UI
        BroadcastUpdate();
    }

    void CreateChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(CHANNEL_ID, "Heart Rate", NotificationImportance.Low);
            var mgr = (NotificationManager)GetSystemService(NotificationService);
            mgr.CreateNotificationChannel(channel);
        }
    }
}