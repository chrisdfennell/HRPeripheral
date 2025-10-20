using Android.App;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Java.Util;
using System;
using System.Diagnostics;

// Add a using alias to resolve the ambiguity between .NET's Debug and Android's Debug.
using Debug = System.Diagnostics.Debug;

namespace HRPeripheral;

public class BlePeripheral : BluetoothGattServerCallback
{
    // Standard UUIDs for Heart Rate Service and Characteristics
    private static readonly UUID UUID_HEART_RATE_SERVICE = UUID.FromString("0000180D-0000-1000-8000-00805f9b34fb");
    private static readonly UUID UUID_HEART_RATE_MEASUREMENT = UUID.FromString("00002A37-0000-1000-8000-00805f9b34fb");
    private static readonly UUID UUID_BODY_SENSOR_LOCATION = UUID.FromString("00002A38-0000-1000-8000-00805f9b34fb");
    private static readonly UUID UUID_CLIENT_CHARACTERISTIC_CONFIGURATION = UUID.FromString("00002902-0000-1000-8000-00805f9b34fb");

    private readonly Context _context;
    private BluetoothManager? _bluetoothManager;
    private BluetoothAdapter? _bluetoothAdapter;
    private BluetoothGattServer? _gattServer;
    private BluetoothLeAdvertiser? _bluetoothLeAdvertiser;
    private BluetoothGattCharacteristic? _hrCharacteristic;
    private readonly AdvertisingCallback _advertisingCallback = new();

    private volatile BluetoothDevice? _subscribedDevice;

    public BlePeripheral(Context context)
    {
        _context = context;
    }

    // -----------------------------------------------------------------------
    // PUBLIC ENTRY POINTS
    // -----------------------------------------------------------------------

    /// <summary>
    /// Start BLE advertising and open a GATT server with Heart Rate Service.
    /// </summary>
    public bool StartAdvertising()
    {
        try
        {
            _bluetoothManager = (BluetoothManager)_context.GetSystemService(Context.BluetoothService)!;
            _bluetoothAdapter = _bluetoothManager.Adapter;

            if (_bluetoothAdapter == null || !_bluetoothAdapter.IsEnabled)
            {
                Debug.WriteLine("Bluetooth adapter missing or disabled.");
                return false;
            }

            // It's better to set the name in the advertising packet itself if possible,
            // but setting it on the adapter is a good fallback.
            _bluetoothAdapter.SetName("HR Monitor");

            if (!_bluetoothAdapter.IsMultipleAdvertisementSupported)
            {
                Debug.WriteLine("BLE advertising not supported.");
                return false;
            }

            // Create GATT Server
            _gattServer = _bluetoothManager.OpenGattServer(_context, this);
            var service = new BluetoothGattService(UUID_HEART_RATE_SERVICE, GattServiceType.Primary);

            _hrCharacteristic = new BluetoothGattCharacteristic(
                UUID_HEART_RATE_MEASUREMENT,
                GattProperty.Notify,
                GattPermission.ReadEncrypted
            );

            var cccDescriptor = new BluetoothGattDescriptor(UUID_CLIENT_CHARACTERISTIC_CONFIGURATION,
                GattDescriptorPermission.ReadEncrypted | GattDescriptorPermission.WriteEncrypted);
            _hrCharacteristic.AddDescriptor(cccDescriptor);

            var bodySensorLocationCharacteristic = new BluetoothGattCharacteristic(
                UUID_BODY_SENSOR_LOCATION,
                GattProperty.Read,
                GattPermission.ReadEncrypted
            );
            bodySensorLocationCharacteristic.SetValue(new byte[] { 1 }); // 1 = Chest

            service.AddCharacteristic(_hrCharacteristic);
            service.AddCharacteristic(bodySensorLocationCharacteristic);
            _gattServer.AddService(service);

            _bluetoothLeAdvertiser = _bluetoothAdapter.BluetoothLeAdvertiser;

            var advertiseData = new AdvertiseData.Builder()
                .AddServiceUuid(new ParcelUuid(UUID_HEART_RATE_SERVICE))
                .SetIncludeTxPowerLevel(false)
                .Build();

            var scanResponse = new AdvertiseData.Builder()
                .SetIncludeDeviceName(true)
                .Build();

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
    /// Stop advertising and close the GATT server.
    /// </summary>
    public void StopAdvertising()
    {
        try { _bluetoothLeAdvertiser?.StopAdvertising(_advertisingCallback); } catch { }
        try { _gattServer?.Close(); } catch { }
        Debug.WriteLine("BLE advertising stopped.");
    }

    /// <summary>
    /// Push updated BPM to subscribed clients.
    /// </summary>
    public void UpdateHeartRate(int bpm)
    {
        if (_gattServer == null || _subscribedDevice == null || _hrCharacteristic == null)
            return;

        byte flags = 0x00;
        byte heartRateValue = (byte)Math.Max(0, Math.Min(255, bpm));

        var payload = new byte[] { flags, heartRateValue };
        _hrCharacteristic.SetValue(payload);

        try
        {
            _gattServer.NotifyCharacteristicChanged(_subscribedDevice, _hrCharacteristic, false);
            Debug.WriteLine($"Sent HR notification: {bpm} bpm");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HR notify error: {ex}");
        }
    }

    // -----------------------------------------------------------------------
    // GATT CALLBACKS
    // -----------------------------------------------------------------------

    public override void OnConnectionStateChange(BluetoothDevice? device, ProfileState status, ProfileState newState)
    {
        base.OnConnectionStateChange(device, status, newState);

        if (newState == ProfileState.Connected)
        {
            Debug.WriteLine($"Device connected: {device?.Address}. Waiting for subscription...");
            // DO NOT set the subscribed device here. Wait for the OnDescriptorWriteRequest.
        }
        else if (newState == ProfileState.Disconnected)
        {
            Debug.WriteLine($"Device disconnected: {device?.Address}");
            // If the disconnected device was our subscriber, clear it.
            if (_subscribedDevice?.Address == device?.Address)
            {
                _subscribedDevice = null;
            }
        }
    }

    public override void OnDescriptorWriteRequest(BluetoothDevice? device, int requestId, BluetoothGattDescriptor? descriptor,
        bool preparedWrite, bool responseNeeded, int offset, byte[]? value)
    {
        base.OnDescriptorWriteRequest(device, requestId, descriptor, preparedWrite, responseNeeded, offset, value);

        Debug.WriteLine($"OnDescriptorWriteRequest from {device?.Address}");

        if (descriptor?.Uuid.Equals(UUID_CLIENT_CHARACTERISTIC_CONFIGURATION) == true)
        {
            // Acknowledge the write request immediately.
            if (responseNeeded)
            {
                _gattServer?.SendResponse(device, requestId, GattStatus.Success, 0, null);
            }

            // Check what value the client wrote.
            var notificationsEnabled = value != null && value.Length > 0 && value[0] == BluetoothGattDescriptor.EnableNotificationValue[0];

            if (notificationsEnabled)
            {
                Debug.WriteLine("Notifications ENABLED by client.");
                _subscribedDevice = device; // THIS IS THE FIX: Only set subscriber after they've asked for data.
            }
            else
            {
                Debug.WriteLine("Notifications DISABLED by client.");
                if (_subscribedDevice?.Address == device?.Address)
                {
                    _subscribedDevice = null;
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // INTERNAL ADVERTISING CALLBACK
    // -----------------------------------------------------------------------
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
}