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

    // =========================
    // Android BLE plumbing
    // =========================
    private readonly Context _context;
    private BluetoothManager? _bluetoothManager;           // Entry to system Bluetooth services
    private BluetoothAdapter? _bluetoothAdapter;           // Radio control (advertising, name, etc.)
    private BluetoothGattServer? _gattServer;              // The GATT *server* hosted by us
    private BluetoothLeAdvertiser? _bluetoothLeAdvertiser; // LE advertising interface

    // Characteristics we expose on our service
    private BluetoothGattCharacteristic? _hrCharacteristic;

    // Our advertiser callback (just logs success/failure)
    private readonly AdvertisingCallback _advertisingCallback = new();

    // The single device currently subscribed to notifications (keep simple)
    // Marked volatile so other threads see updates (BLE callbacks can be off main thread)
    private volatile BluetoothDevice? _subscribedDevice;

    // =========================
    // Known devices persistence
    // =========================
    private const string PREFS_NAME = "hrp_ble"; // namespace only (we use default shared prefs)
    private const string KEY_KNOWN = "known_devices";

    private ISharedPreferences? _prefs;
    private readonly HashSet<string> _knownDevices = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Read-only view of known device MAC addresses (e.g., show in Settings).
    /// </summary>
    public IReadOnlyCollection<string> KnownDevices => _knownDevices.ToList().AsReadOnly();

    public BlePeripheral(Context context)
    {
        _context = context;

        // Load known device addresses from shared preferences
        _prefs = PreferenceManager.GetDefaultSharedPreferences(_context);
        var csv = _prefs?.GetString(KEY_KNOWN, string.Empty) ?? string.Empty;
        foreach (var a in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            _knownDevices.Add(a.Trim());
    }

    private void SaveKnownDevices()
    {
        try
        {
            var csv = string.Join(",", _knownDevices);
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
                return false;
            }

            // Optional: set a friendly adapter name (scan response will include device name)
            _bluetoothAdapter.SetName("HR Monitor");

            if (!_bluetoothAdapter.IsMultipleAdvertisementSupported)
            {
                Debug.WriteLine("BLE advertising not supported.");
                return false;
            }

            // 2) Build our GATT server and service/characteristics tree.
            _gattServer = _bluetoothManager.OpenGattServer(_context, this);

            // Primary Heart Rate Service
            var service = new BluetoothGattService(UUID_HEART_RATE_SERVICE, GattServiceType.Primary);

            // Heart Rate Measurement characteristic:
            //   • NOTIFY only (no read/write by spec)
            //   • CCCD lets a client enable/disable notifications.
            _hrCharacteristic = new BluetoothGattCharacteristic(
                UUID_HEART_RATE_MEASUREMENT,
                GattProperty.Notify,
                GattPermission.ReadEncrypted // conservative; value isn't read directly
            );

            // CCC descriptor (enables notifications/indications at the client’s request)
            var cccDescriptor = new BluetoothGattDescriptor(
                UUID_CLIENT_CHARACTERISTIC_CONFIGURATION,
                GattDescriptorPermission.ReadEncrypted | GattDescriptorPermission.WriteEncrypted
            );
            _hrCharacteristic.AddDescriptor(cccDescriptor);

            // Body Sensor Location characteristic (simple read-only; 1 = Chest)
            var bodySensorLocationCharacteristic = new BluetoothGattCharacteristic(
                UUID_BODY_SENSOR_LOCATION,
                GattProperty.Read,
                GattPermission.ReadEncrypted
            );
            bodySensorLocationCharacteristic.SetValue(new byte[] { 1 }); // 1=Chest (spec-defined values)

            // Add characteristics & service
            service.AddCharacteristic(_hrCharacteristic);
            service.AddCharacteristic(bodySensorLocationCharacteristic);
            _gattServer.AddService(service);

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

        _subscribedDevice = null; // drop any subscriber reference on shutdown
        Debug.WriteLine("BLE advertising stopped.");
    }

    /// <summary>
    /// Sends a heart rate notification to the currently subscribed client (if any).
    /// Does nothing until a client writes to CCCD to enable notifications.
    /// </summary>
    public void UpdateHeartRate(int bpm)
    {
        // Guard: we need (a) a server, (b) a subscriber, (c) the measurement characteristic
        if (_gattServer == null || _subscribedDevice == null || _hrCharacteristic == null)
            return;

        // Spec: flags in byte0. We send simplest form:
        //   bit0 = 0 → HR value is UInt8 in byte1
        byte flags = 0x00;

        // Clamp to UInt8 since we advertise the UInt8 format (0..255)
        byte heartRateValue = (byte)Math.Max(0, Math.Min(255, bpm));

        // Payload = [flags, bpm]
        var payload = new byte[] { flags, heartRateValue };
        _hrCharacteristic.SetValue(payload);

        try
        {
            // third parameter (confirm) = false → it's a notification, not indication
            _gattServer.NotifyCharacteristicChanged(_subscribedDevice, _hrCharacteristic, false);
            Debug.WriteLine($"Sent HR notification: {bpm} bpm");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HR notify error: {ex}");
        }
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

            if (adapter != null)
            {
                foreach (var addr in _knownDevices.ToList())
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

            _subscribedDevice = null;
            _knownDevices.Clear();
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

            if (_subscribedDevice?.Address.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
                _subscribedDevice = null;

            try { if (dev != null) _gattServer?.CancelConnection(dev); } catch { /* ignore */ }

            if (alsoUnbond && dev != null && dev.BondState == Bond.Bonded)
                TryRemoveBond(dev);

            _knownDevices.Remove(address);
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

            // Track as "known"
            if (device?.Address != null)
            {
                _knownDevices.Add(device.Address);
                SaveKnownDevices();
            }
        }
        else if (newState == ProfileState.Disconnected)
        {
            Debug.WriteLine($"Device disconnected: {addr}");

            // If the disconnected device was our current subscriber, clear it
            if (_subscribedDevice?.Address == device?.Address)
            {
                _subscribedDevice = null;
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

        // Only interested in writes to CCCD of our HR Measurement characteristic
        if (descriptor?.Uuid.Equals(UUID_CLIENT_CHARACTERISTIC_CONFIGURATION) == true)
        {
            // Always ACK first if the client requested a response
            if (responseNeeded)
            {
                _gattServer?.SendResponse(device, requestId, GattStatus.Success, 0, null);
            }

            // Per spec, enabling notifications writes 0x01:00 (little-endian) to CCCD.
            // We check the first byte to keep it simple.
            bool notificationsEnabled =
                value != null &&
                value.Length > 0 &&
                value[0] == BluetoothGattDescriptor.EnableNotificationValue[0];

            if (notificationsEnabled)
            {
                Debug.WriteLine("Notifications ENABLED by client.");
                // Mark this device as our active subscriber (we support one for simplicity).
                // This is the correct moment to start sending UpdateHeartRate().
                _subscribedDevice = device;
            }
            else
            {
                Debug.WriteLine("Notifications DISABLED by client.");
                if (_subscribedDevice?.Address == device?.Address)
                {
                    _subscribedDevice = null; // stop sending updates to this device
                }
            }
        }
    }

    // =====================================================================
    // Advertising callback wrapper (logs for observability)
    // =====================================================================
    private class AdvertisingCallback : AdvertiseCallback
    {
        public override void OnStartFailure(AdvertiseFailure errorCode)
        {
            Debug.WriteLine($"Advertising failed: {errorCode}");
        }

        public override void OnStartSuccess(AdvertiseSettings? settingsInEffect)
        {
            Debug.WriteLine("Advertising success!");
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