using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Java.Util;
using Debug = System.Diagnostics.Debug;

namespace HRPeripheral.Companion;

/// <summary>
/// Foreground service that acts as a BLE Central: connects to a Heart Rate peripheral,
/// subscribes to HR Measurement and Battery Level notifications, and broadcasts
/// updates to the UI via Intents.
/// </summary>
[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeConnectedDevice
)]
public class BleCentralService : Service
{
    public const string CHANNEL_ID = "hr_companion_channel";
    public const string ACTION_HR_UPDATE = "com.fennell.hrperipheral.companion.HR_UPDATE";
    public const string ACTION_CONNECTION = "com.fennell.hrperipheral.companion.CONNECTION";
    public const string EXTRA_DEVICE_ADDRESS = "device_address";

    // Standard BLE UUIDs
    private static readonly UUID UUID_HR_SERVICE = UUID.FromString("0000180D-0000-1000-8000-00805f9b34fb");
    private static readonly UUID UUID_HR_MEASUREMENT = UUID.FromString("00002A37-0000-1000-8000-00805f9b34fb");
    private static readonly UUID UUID_BATTERY_SERVICE = UUID.FromString("0000180F-0000-1000-8000-00805f9b34fb");
    private static readonly UUID UUID_BATTERY_LEVEL = UUID.FromString("00002A19-0000-1000-8000-00805f9b34fb");
    private static readonly UUID UUID_CCCD = UUID.FromString("00002902-0000-1000-8000-00805f9b34fb");

    private BluetoothManager? _btManager;
    private BluetoothGatt? _gatt;
    private GattClientCallback? _gattCallback;
    private string? _targetAddress;
    private bool _connected;

    // Reconnect state
    private Handler? _handler;
    private readonly ReconnectBackoff _backoff = new();

    public override void OnCreate()
    {
        base.OnCreate();
        _btManager = (BluetoothManager)GetSystemService(BluetoothService)!;
        _handler = new Handler(Looper.MainLooper);
        CreateChannel();
        UpdateNotification("Waiting to connect...");
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var address = intent?.GetStringExtra(EXTRA_DEVICE_ADDRESS);
        if (!string.IsNullOrEmpty(address))
        {
            _targetAddress = address;
            ConnectToDevice(address);
        }
        return StartCommandResult.Sticky;
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        _handler?.RemoveCallbacksAndMessages(null);
        try { _gatt?.Close(); } catch { }
        _gatt = null;
        _connected = false;
        try { StopForeground(true); } catch { }
        base.OnDestroy();
    }

    private void ConnectToDevice(string address)
    {
        var adapter = _btManager?.Adapter;
        if (adapter == null || !adapter.IsEnabled)
        {
            Debug.WriteLine("Bluetooth adapter missing or disabled.");
            return;
        }

        try
        {
            var device = adapter.GetRemoteDevice(address);
            if (device == null)
            {
                Debug.WriteLine($"Device not found: {address}");
                return;
            }

            _gattCallback = new GattClientCallback(this);
            _gatt = device.ConnectGatt(this, false, _gattCallback, BluetoothTransports.Le);
            UpdateNotification($"Connecting to {address}...");
            Debug.WriteLine($"Connecting to {address}...");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ConnectToDevice error: {ex}");
        }
    }

    private void OnConnected()
    {
        _connected = true;
        _backoff.Reset();
        UpdateNotification("Connected");
        BroadcastConnectionState(true);
        _gatt?.DiscoverServices();
    }

    private void OnDisconnected()
    {
        _connected = false;
        UpdateNotification("Disconnected — reconnecting...");
        BroadcastConnectionState(false);
        ScheduleReconnect();
    }

    private void ScheduleReconnect()
    {
        if (string.IsNullOrEmpty(_targetAddress)) return;

        var addr = _targetAddress;
        int delayMs = _backoff.NextDelayMs();
        _handler?.PostDelayed(() =>
        {
            if (!_connected)
            {
                Debug.WriteLine($"Reconnecting to {addr} (attempt={_backoff.Attempt}, delay={delayMs}ms)...");
                try { _gatt?.Close(); } catch { }
                ConnectToDevice(addr);
            }
        }, delayMs);
    }

    private void OnServicesDiscovered()
    {
        if (_gatt == null) return;

        // Subscribe to HR Measurement
        var hrService = _gatt.GetService(UUID_HR_SERVICE);
        var hrMeas = hrService?.GetCharacteristic(UUID_HR_MEASUREMENT);
        if (hrMeas != null)
        {
            _gatt.SetCharacteristicNotification(hrMeas, true);
            var cccd = hrMeas.GetDescriptor(UUID_CCCD);
            if (cccd != null)
            {
                cccd.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
                _gatt.WriteDescriptor(cccd);
                Debug.WriteLine("Subscribed to HR Measurement notifications.");
            }
        }

        // Subscribe to Battery Level (if available)
        var battService = _gatt.GetService(UUID_BATTERY_SERVICE);
        var battLevel = battService?.GetCharacteristic(UUID_BATTERY_LEVEL);
        if (battLevel != null)
        {
            // Queue this after the HR CCCD write completes (Android handles sequencing)
            _handler?.PostDelayed(() =>
            {
                if (_gatt == null) return;
                _gatt.SetCharacteristicNotification(battLevel, true);
                var cccd = battLevel.GetDescriptor(UUID_CCCD);
                if (cccd != null)
                {
                    cccd.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
                    _gatt.WriteDescriptor(cccd);
                    Debug.WriteLine("Subscribed to Battery Level notifications.");
                }

                // Also read the battery level immediately
                _gatt.ReadCharacteristic(battLevel);
            }, 500);
        }
    }

    private void OnHeartRateReceived(int bpm)
    {
        var intent = new Intent(ACTION_HR_UPDATE);
        intent.PutExtra("hr", bpm);
        intent.SetPackage(PackageName);
        try { SendBroadcast(intent); } catch { }

        UpdateNotification($"HR: {bpm} bpm");
    }

    private void OnBatteryLevelReceived(int level)
    {
        var intent = new Intent(ACTION_HR_UPDATE);
        intent.PutExtra("battery", level);
        intent.SetPackage(PackageName);
        try { SendBroadcast(intent); } catch { }
    }

    private void BroadcastConnectionState(bool connected)
    {
        var intent = new Intent(ACTION_CONNECTION);
        intent.PutExtra("connected", connected);
        intent.SetPackage(PackageName);
        try { SendBroadcast(intent); } catch { }
    }

    private void CreateChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(CHANNEL_ID, "HR Companion", NotificationImportance.Low);
            var mgr = (NotificationManager?)GetSystemService(NotificationService);
            mgr?.CreateNotificationChannel(channel);
        }
    }

    private void UpdateNotification(string text)
    {
        try
        {
            var notif = new Notification.Builder(this, CHANNEL_ID)
                .SetContentTitle("HR Companion")
                .SetContentText(text)
                .SetSmallIcon(Android.Resource.Drawable.StatSysDataBluetooth)
                .SetOngoing(true)
                .Build();
            StartForeground(1, notif);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Notification error: {ex}");
        }
    }

    // =====================================================================
    // GATT Client Callback
    // =====================================================================
    private class GattClientCallback : BluetoothGattCallback
    {
        private readonly BleCentralService _svc;
        public GattClientCallback(BleCentralService svc) => _svc = svc;

        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
        {
            base.OnConnectionStateChange(gatt, status, newState);
            Debug.WriteLine($"GATT connection state: {newState} (status={status})");

            if (newState == ProfileState.Connected)
                _svc.OnConnected();
            else if (newState == ProfileState.Disconnected)
                _svc.OnDisconnected();
        }

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
        {
            base.OnServicesDiscovered(gatt, status);
            Debug.WriteLine($"Services discovered: status={status}");

            if (status == GattStatus.Success)
                _svc.OnServicesDiscovered();
        }

        public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
        {
            base.OnCharacteristicChanged(gatt, characteristic);

            if (characteristic?.Uuid?.Equals(UUID_HR_MEASUREMENT) == true)
            {
                int bpm = HrPayload.Parse(characteristic.GetValue());
                if (bpm > 0)
                    _svc.OnHeartRateReceived(bpm);
            }
            else if (characteristic?.Uuid?.Equals(UUID_BATTERY_LEVEL) == true)
            {
                var val = characteristic.GetValue();
                int level = (val != null && val.Length > 0) ? val[0] : -1;
                if (level >= 0)
                    _svc.OnBatteryLevelReceived(level);
            }
        }

        public override void OnCharacteristicRead(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status)
        {
            base.OnCharacteristicRead(gatt, characteristic, status);
            if (status != GattStatus.Success || characteristic == null) return;

            if (characteristic.Uuid?.Equals(UUID_BATTERY_LEVEL) == true)
            {
                var val = characteristic.GetValue();
                int level = (val != null && val.Length > 0) ? val[0] : -1;
                if (level >= 0)
                    _svc.OnBatteryLevelReceived(level);
            }
        }
    }
}
