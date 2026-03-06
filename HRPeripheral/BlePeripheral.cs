using Android.App;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Android.Preferences; // PreferenceManager / ISharedPreferences
using Java.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

// Avoid ambiguity (Android.Util.Debug vs System.Diagnostics.Debug)
using Debug = System.Diagnostics.Debug;

namespace HRPeripheral;

/// <summary>
/// A minimal BLE *peripheral* (GATT server) that exposes the standard
/// Heart Rate Service (0x180D) and streams Heart Rate Measurement notifications
/// (0x2A37) to a central (phone/watch) after it subscribes.
/// 
/// Adds:
///  • Tracks previously connected device addresses (KnownDevices)
///  • ForgetDevice / ForgetAllDevices with optional OS unbond
/// </summary>
public class BlePeripheral : BluetoothGattServerCallback
{
    // =========================
    // Standard SIG UUIDs
    // =========================
    private static readonly UUID UUID_HEART_RATE_SERVICE = UUID.FromString("0000180D-0000-1000-8000-00805f9b34fb"); // 0x180D
    private static readonly UUID UUID_HEART_RATE_MEASUREMENT = UUID.FromString("00002A37-0000-1000-8000-00805f9b34fb"); // 0x2A37
    private static readonly UUID UUID_BODY_SENSOR_LOCATION = UUID.FromString("00002A38-0000-1000-8000-00805f9b34fb"); // 0x2A38
    private static readonly UUID UUID_CLIENT_CHARACTERISTIC_CONFIGURATION = UUID.FromString("00002902-0000-1000-8000-00805f9b34fb"); // CCCD

    // Battery Service UUIDs (SIG-standard)
    private static readonly UUID UUID_BATTERY_SERVICE = UUID.FromString("0000180F-0000-1000-8000-00805f9b34fb"); // 0x180F
    private static readonly UUID UUID_BATTERY_LEVEL = UUID.FromString("00002A19-0000-1000-8000-00805f9b34fb");   // 0x2A19

    // =========================
    // Android BLE plumbing
    // =========================
    private readonly Context _context;
    private BluetoothManager? _bluetoothManager;           // Entry to system Bluetooth services
    private BluetoothAdapter? _bluetoothAdapter;           // Radio control (advertising, name, etc.)
    private BluetoothGattServer? _gattServer;              // The GATT *server* hosted by us
    private BluetoothLeAdvertiser? _bluetoothLeAdvertiser; // LE advertising interface

    // Characteristics we expose on our services
    private BluetoothGattCharacteristic? _hrCharacteristic;
    private BluetoothGattCharacteristic? _batteryCharacteristic;

    // Battery notification subscribers (same pattern as HR)
    private readonly Dictionary<string, BluetoothDevice> _batterySubscribers = new(StringComparer.OrdinalIgnoreCase);

    // Our advertiser callback (surfaces success/failure via events)
    private readonly AdvertisingCallback _advertisingCallback;

    // Pending HR service — added after Battery Service via OnServiceAdded chaining
    private BluetoothGattService? _pendingHrService;

    // All devices currently subscribed to HR notifications.
    // Protected by _lock (BLE callbacks can be off main thread).
    private readonly Dictionary<string, BluetoothDevice> _subscribedDevices = new(StringComparer.OrdinalIgnoreCase);

    // =========================
    // Events for service/UI feedback
    // =========================
    /// <summary>Raised when an error occurs (advertising failure, notify error, etc.).</summary>
    public event Action<string>? OnError;

    /// <summary>Raised when BLE status changes (connected, subscribed, disconnected, etc.).</summary>
    public event Action<string>? OnStatusChanged;

    // =========================
    // Known devices persistence
    // =========================
    private const string PREFS_NAME = "hrp_ble"; // namespace only (we use default shared prefs)
    private const string KEY_KNOWN = "known_devices";

    private ISharedPreferences? _prefs;
    private readonly HashSet<string> _knownDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>
    /// Read-only snapshot of known device MAC addresses (e.g., show in Settings).
    /// </summary>
    public IReadOnlyCollection<string> KnownDevices
    {
        get { lock (_lock) { return _knownDevices.ToList().AsReadOnly(); } }
    }

    public BlePeripheral(Context context)
    {
        _context = context;
        _advertisingCallback = new AdvertisingCallback(this);

        // Load known device addresses from shared preferences
        _prefs = PreferenceManager.GetDefaultSharedPreferences(_context);
        var csv = _prefs?.GetString(KEY_KNOWN, string.Empty) ?? string.Empty;
        lock (_lock)
        {
            foreach (var a in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                _knownDevices.Add(a.Trim());
        }
    }

    private void SaveKnownDevices()
    {
        try
        {
            string csv;
            lock (_lock) { csv = string.Join(",", _knownDevices); }
            _prefs?.Edit()?.PutString(KEY_KNOWN, csv)?.Apply();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SaveKnownDevices error: {ex}");
        }
    }

    // =====================================================================
    // PUBLIC ENTRY POINTS
    // =====================================================================

    /// <summary>
    /// Starts BLE advertising and creates a GATT server exposing the HR service.
    /// Returns false if any preconditions fail (radio off, no advertiser, etc.).
    /// </summary>
    public bool StartAdvertising()
    {
        try
        {
            // 1) Grab manager/adapter; abort if Bluetooth is off or missing.
            _bluetoothManager = (BluetoothManager)_context.GetSystemService(Context.BluetoothService)!;
            _bluetoothAdapter = _bluetoothManager.Adapter;

            if (_bluetoothAdapter == null || !_bluetoothAdapter.IsEnabled)
            {
                Debug.WriteLine("Bluetooth adapter missing or disabled.");
                OnError?.Invoke("Bluetooth adapter missing or disabled");
                return false;
            }

            // Optional: set a friendly adapter name (scan response will include device name)
            _bluetoothAdapter.SetName("HR Monitor");

            if (!_bluetoothAdapter.IsMultipleAdvertisementSupported)
            {
                Debug.WriteLine("BLE advertising not supported.");
                OnError?.Invoke("BLE advertising not supported on this device");
                return false;
            }

            // 2) Build our GATT server and service/characteristics tree.
            _gattServer = _bluetoothManager.OpenGattServer(_context, this);

            // --- Build Heart Rate Service (added second, via OnServiceAdded chain) ---
            var hrService = new BluetoothGattService(UUID_HEART_RATE_SERVICE, GattServiceType.Primary);

            _hrCharacteristic = new BluetoothGattCharacteristic(
                UUID_HEART_RATE_MEASUREMENT,
                GattProperty.Notify,
                GattPermission.ReadEncrypted
            );
            var hrCccd = new BluetoothGattDescriptor(
                UUID_CLIENT_CHARACTERISTIC_CONFIGURATION,
                GattDescriptorPermission.ReadEncrypted | GattDescriptorPermission.WriteEncrypted
            );
            _hrCharacteristic.AddDescriptor(hrCccd);

            var bodySensorLocation = new BluetoothGattCharacteristic(
                UUID_BODY_SENSOR_LOCATION,
                GattProperty.Read,
                GattPermission.ReadEncrypted
            );
            bodySensorLocation.SetValue(new byte[] { 1 }); // 1=Chest

            hrService.AddCharacteristic(_hrCharacteristic);
            hrService.AddCharacteristic(bodySensorLocation);

            // --- Build Battery Service (added first) ---
            var batteryService = new BluetoothGattService(UUID_BATTERY_SERVICE, GattServiceType.Primary);

            _batteryCharacteristic = new BluetoothGattCharacteristic(
                UUID_BATTERY_LEVEL,
                GattProperty.Read | GattProperty.Notify,
                GattPermission.ReadEncrypted
            );
            _batteryCharacteristic.SetValue(new byte[] { (byte)GetBatteryLevel() });

            var batteryCccd = new BluetoothGattDescriptor(
                UUID_CLIENT_CHARACTERISTIC_CONFIGURATION,
                GattDescriptorPermission.ReadEncrypted | GattDescriptorPermission.WriteEncrypted
            );
            _batteryCharacteristic.AddDescriptor(batteryCccd);
            batteryService.AddCharacteristic(_batteryCharacteristic);

            // Chain: add Battery Service first, then HR Service in OnServiceAdded callback
            _pendingHrService = hrService;
            _gattServer.AddService(batteryService);

            // 3) Start advertising the Heart Rate Service UUID
            _bluetoothLeAdvertiser = _bluetoothAdapter.BluetoothLeAdvertiser;

            // Advertising payload: include service UUID so centrals can filter
            var advertiseData = new AdvertiseData.Builder()
                .AddServiceUuid(new ParcelUuid(UUID_HEART_RATE_SERVICE))
                .SetIncludeTxPowerLevel(false)   // keep packet small
                .Build();

            // Scan response: include device name (shown in scanners)
            var scanResponse = new AdvertiseData.Builder()
                .SetIncludeDeviceName(true)
                .Build();

            // Advertising settings: fast & connectable
            var settings = new AdvertiseSettings.Builder()
                .SetAdvertiseMode(AdvertiseMode.LowLatency)
                .SetTxPowerLevel(AdvertiseTx.PowerHigh)
                .SetConnectable(true)
                .Build();

            _bluetoothLeAdvertiser.StartAdvertising(settings, advertiseData, scanResponse, _advertisingCallback);
            Debug.WriteLine("BLE advertising started successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BLE start error: {ex}");
            OnError?.Invoke($"BLE start error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stops advertising and closes the GATT server. Safe to call multiple times.
    /// </summary>
    public void StopAdvertising()
    {
        try { _bluetoothLeAdvertiser?.StopAdvertising(_advertisingCallback); } catch { /* ignore */ }
        try { _gattServer?.Close(); } catch { /* ignore */ }

        lock (_lock)
        {
            _subscribedDevices.Clear();
            _batterySubscribers.Clear();
        }
        Debug.WriteLine("BLE advertising stopped.");
    }

    /// <summary>
    /// Sends a heart rate notification to the currently subscribed client (if any).
    /// Does nothing until a client writes to CCCD to enable notifications.
    /// </summary>
    public void UpdateHeartRate(int bpm)
    {
        // Snapshot all subscribers under lock to avoid TOCTOU race
        List<BluetoothDevice> subscribers;
        lock (_lock)
        {
            if (_subscribedDevices.Count == 0) return;
            subscribers = new List<BluetoothDevice>(_subscribedDevices.Values);
        }

        if (_gattServer == null || _hrCharacteristic == null)
            return;

        var payload = HrPayload.Build(bpm);
        _hrCharacteristic.SetValue(payload);

        foreach (var subscriber in subscribers)
        {
            try
            {
                _gattServer.NotifyCharacteristicChanged(subscriber, _hrCharacteristic, false);
                Debug.WriteLine($"Sent HR notification: {bpm} bpm to {subscriber.Address}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HR notify error for {subscriber.Address}: {ex}");
            }
        }
    }

    /// <summary>Number of currently subscribed centrals.</summary>
    public int SubscriberCount
    {
        get { lock (_lock) { return _subscribedDevices.Count; } }
    }

    // =====================================================================
    // "Forget devices" — public APIs for Settings
    // =====================================================================

    /// <summary>
    /// Forget ALL known devices. Optionally attempt to unpair/remove bond for each.
    /// Also cancels any active GATT connection and drops the subscriber.
    /// </summary>
    /// <param name="alsoUnbond">If true, attempt to remove OS bond via reflection.</param>
    public void ForgetAllDevices(bool alsoUnbond = false)
    {
        try
        {
            var adapter = _bluetoothManager?.Adapter;

            List<string> addresses;
            lock (_lock) { addresses = _knownDevices.ToList(); }

            if (adapter != null)
            {
                foreach (var addr in addresses)
                {
                    BluetoothDevice? dev = null;
                    try { dev = adapter.GetRemoteDevice(addr); } catch { /* ignore invalid addr */ }

                    // Cancel any server-side connection
                    try { if (dev != null) _gattServer?.CancelConnection(dev); } catch { /* ignore */ }

                    // Optional: unbond (hidden API; best-effort)
                    if (alsoUnbond && dev != null && dev.BondState == Bond.Bonded)
                        TryRemoveBond(dev);
                }
            }

            lock (_lock)
            {
                _subscribedDevices.Clear();
                _batterySubscribers.Clear();
                _knownDevices.Clear();
            }
            SaveKnownDevices();

            Debug.WriteLine("All known devices forgotten.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ForgetAllDevices error: {ex}");
        }
    }

    /// <summary>
    /// Forget a single device by MAC address. Optionally attempts OS unpair.
    /// </summary>
    public void ForgetDevice(string address, bool alsoUnbond = false)
    {
        if (string.IsNullOrWhiteSpace(address)) return;

        try
        {
            var adapter = _bluetoothManager?.Adapter;
            BluetoothDevice? dev = null;

            if (adapter != null)
            {
                try { dev = adapter.GetRemoteDevice(address); } catch { /* ignore */ }
            }

            lock (_lock)
            {
                _subscribedDevices.Remove(address);
                _batterySubscribers.Remove(address);
                _knownDevices.Remove(address);
            }

            try { if (dev != null) _gattServer?.CancelConnection(dev); } catch { /* ignore */ }

            if (alsoUnbond && dev != null && dev.BondState == Bond.Bonded)
                TryRemoveBond(dev);

            SaveKnownDevices();

            Debug.WriteLine($"Device {address} forgotten.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ForgetDevice error: {ex}");
        }
    }

    private static void TryRemoveBond(BluetoothDevice dev)
    {
        try
        {
            // Hidden API reflection: dev.removeBond()
            var method = dev.Class.GetMethod("removeBond");
            method?.Invoke(dev);
            Debug.WriteLine($"Requested unbond for {dev.Address}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Unbond (removeBond) failed for {dev.Address}: {ex.Message}");
        }
    }

    // =====================================================================
    // GATT SERVER CALLBACKS (lifecycle + subscriptions)
    // =====================================================================

    /// <summary>
    /// Connection state changes for a remote central (phone/watch).
    /// NOTE: Do not treat "connected" as "subscribed"—wait for CCCD write.
    /// </summary>
    public override void OnConnectionStateChange(BluetoothDevice? device, ProfileState status, ProfileState newState)
    {
        base.OnConnectionStateChange(device, status, newState);

        var addr = device?.Address ?? "(unknown)";
        if (newState == ProfileState.Connected)
        {
            Debug.WriteLine($"Device connected: {addr}. Waiting for subscription...");
            OnStatusChanged?.Invoke($"Device connected: {addr}");

            // Track as "known"
            if (device?.Address != null)
            {
                lock (_lock) { _knownDevices.Add(device.Address); }
                SaveKnownDevices();
            }
        }
        else if (newState == ProfileState.Disconnected)
        {
            Debug.WriteLine($"Device disconnected: {addr}");
            OnStatusChanged?.Invoke($"Device disconnected: {addr}");

            if (device?.Address != null)
            {
                lock (_lock)
                {
                    _subscribedDevices.Remove(device.Address);
                    _batterySubscribers.Remove(device.Address);
                }
            }
        }
    }

    /// <summary>
    /// Called when a descriptor (like CCCD) is written by the central.
    /// This is where we learn whether notifications are enabled/disabled.
    /// </summary>
    public override void OnDescriptorWriteRequest(
        BluetoothDevice? device,
        int requestId,
        BluetoothGattDescriptor? descriptor,
        bool preparedWrite,
        bool responseNeeded,
        int offset,
        byte[]? value)
    {
        base.OnDescriptorWriteRequest(device, requestId, descriptor, preparedWrite, responseNeeded, offset, value);

        Debug.WriteLine($"OnDescriptorWriteRequest from {device?.Address}");

        if (descriptor?.Uuid.Equals(UUID_CLIENT_CHARACTERISTIC_CONFIGURATION) != true)
            return;

        // Always ACK first if the client requested a response
        if (responseNeeded)
            _gattServer?.SendResponse(device, requestId, GattStatus.Success, 0, null);

        bool notificationsEnabled =
            value != null &&
            value.Length > 0 &&
            value[0] == BluetoothGattDescriptor.EnableNotificationValue[0];

        // Determine which characteristic this CCCD belongs to
        var parentUuid = descriptor.Characteristic?.Uuid;

        if (parentUuid?.Equals(UUID_HEART_RATE_MEASUREMENT) == true)
            HandleHrSubscription(device, notificationsEnabled);
        else if (parentUuid?.Equals(UUID_BATTERY_LEVEL) == true)
            HandleBatterySubscription(device, notificationsEnabled);
    }

    private void HandleHrSubscription(BluetoothDevice? device, bool enabled)
    {
        if (enabled)
        {
            Debug.WriteLine($"HR notifications ENABLED by {device?.Address}.");
            if (device?.Address != null)
                lock (_lock) { _subscribedDevices[device.Address] = device; }
            OnStatusChanged?.Invoke($"Client subscribed: {device?.Address}");
        }
        else
        {
            Debug.WriteLine($"HR notifications DISABLED by {device?.Address}.");
            if (device?.Address != null)
                lock (_lock) { _subscribedDevices.Remove(device.Address); }
            OnStatusChanged?.Invoke($"Client unsubscribed: {device?.Address}");
        }
    }

    private void HandleBatterySubscription(BluetoothDevice? device, bool enabled)
    {
        if (device?.Address == null) return;
        if (enabled)
        {
            lock (_lock) { _batterySubscribers[device.Address] = device; }
            Debug.WriteLine($"Battery notifications ENABLED for {device.Address}");
        }
        else
        {
            lock (_lock) { _batterySubscribers.Remove(device.Address); }
            Debug.WriteLine($"Battery notifications DISABLED for {device.Address}");
        }
    }

    // =====================================================================
    // GATT SERVICE CHAINING
    // =====================================================================

    /// <summary>
    /// Called after each AddService completes. Chains the HR service after Battery.
    /// </summary>
    public override void OnServiceAdded(GattStatus status, BluetoothGattService? service)
    {
        base.OnServiceAdded(status, service);
        Debug.WriteLine($"OnServiceAdded: {service?.Uuid} status={status}");

        if (service?.Uuid?.Equals(UUID_BATTERY_SERVICE) == true && _pendingHrService != null)
        {
            _gattServer?.AddService(_pendingHrService);
            _pendingHrService = null;
        }
    }

    /// <summary>
    /// Handles read requests for Battery Level and Body Sensor Location characteristics.
    /// </summary>
    public override void OnCharacteristicReadRequest(
        BluetoothDevice? device, int requestId, int offset,
        BluetoothGattCharacteristic? characteristic)
    {
        base.OnCharacteristicReadRequest(device, requestId, offset, characteristic);

        if (characteristic?.Uuid?.Equals(UUID_BATTERY_LEVEL) == true)
        {
            int level = GetBatteryLevel();
            _gattServer?.SendResponse(device, requestId, GattStatus.Success, 0, new byte[] { (byte)level });
            Debug.WriteLine($"Battery read request from {device?.Address}: {level}%");
        }
        else if (characteristic?.Uuid?.Equals(UUID_BODY_SENSOR_LOCATION) == true)
        {
            _gattServer?.SendResponse(device, requestId, GattStatus.Success, 0, new byte[] { 1 });
        }
        else
        {
            _gattServer?.SendResponse(device, requestId, GattStatus.RequestNotSupported, 0, null);
        }
    }

    // =====================================================================
    // BATTERY SERVICE
    // =====================================================================

    private int GetBatteryLevel()
    {
        try
        {
            using var intentFilter = new IntentFilter(Intent.ActionBatteryChanged);
            using var batteryStatus = _context.RegisterReceiver(null, intentFilter);
            if (batteryStatus == null) return 100;

            int level = batteryStatus.GetIntExtra(BatteryManager.ExtraLevel, -1);
            int scale = batteryStatus.GetIntExtra(BatteryManager.ExtraScale, -1);

            if (level >= 0 && scale > 0)
                return (int)Math.Round(100.0 * level / scale);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetBatteryLevel error: {ex}");
        }
        return 100;
    }

    /// <summary>
    /// Reads the current battery level and notifies all subscribed centrals.
    /// Called periodically by HeartRateService.
    /// </summary>
    public void UpdateBatteryLevel()
    {
        List<BluetoothDevice> subscribers;
        lock (_lock)
        {
            if (_batterySubscribers.Count == 0) return;
            subscribers = new List<BluetoothDevice>(_batterySubscribers.Values);
        }

        if (_gattServer == null || _batteryCharacteristic == null) return;

        int level = GetBatteryLevel();
        _batteryCharacteristic.SetValue(new byte[] { (byte)level });

        foreach (var subscriber in subscribers)
        {
            try
            {
                _gattServer.NotifyCharacteristicChanged(subscriber, _batteryCharacteristic, false);
                Debug.WriteLine($"Sent battery notification: {level}% to {subscriber.Address}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Battery notify error for {subscriber.Address}: {ex}");
            }
        }
    }

    // =====================================================================
    // Advertising callback wrapper (logs for observability)
    // =====================================================================
    private class AdvertisingCallback : AdvertiseCallback
    {
        private readonly BlePeripheral _parent;
        public AdvertisingCallback(BlePeripheral parent) => _parent = parent;

        public override void OnStartFailure(AdvertiseFailure errorCode)
        {
            Debug.WriteLine($"Advertising failed: {errorCode}");
            _parent.OnError?.Invoke($"BLE advertising failed: {errorCode}");
        }

        public override void OnStartSuccess(AdvertiseSettings? settingsInEffect)
        {
            Debug.WriteLine("Advertising success!");
            _parent.OnStatusChanged?.Invoke("Advertising started");
        }
    }

    /// <summary>
    /// Back-compat shim: older code called NotifySubscribers(bpm).
    /// This simply forwards to UpdateHeartRate(bpm).
    /// </summary>
    public void NotifySubscribers(int bpm)
    {
        UpdateHeartRate(bpm);
    }

}