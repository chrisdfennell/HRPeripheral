using Android.App;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Java.Util;
using System;

namespace HRPeripheral;

public class BlePeripheral : BluetoothGattServerCallback
{
    // Standard UUIDs for Heart Rate Service and Characteristics
    static readonly UUID UUID_HRS = UUID.FromString("0000180D-0000-1000-8000-00805F9B34FB");
    static readonly UUID UUID_HR_MEAS = UUID.FromString("00002A37-0000-1000-8000-00805F9B34FB");
    static readonly UUID UUID_BODY_LOC = UUID.FromString("00002A38-0000-1000-8000-00805F9B34FB");
    static readonly UUID UUID_CCC = UUID.FromString("00002902-0000-1000-8000-00805F9B34FB");

    private readonly Context _ctx;
    private BluetoothManager? _mgr;
    private BluetoothAdapter? _adapter;
    private BluetoothGattServer? _server;
    private BluetoothLeAdvertiser? _adv;
    private BluetoothGattCharacteristic? _hrChar;
    private readonly AdvCb _callback = new();

    private volatile BluetoothDevice? _subscribed;

    public BlePeripheral(Context ctx)
    {
        _ctx = ctx;
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
            _mgr = (BluetoothManager)_ctx.GetSystemService(Context.BluetoothService)!;
            _adapter = _mgr.Adapter;

            if (_adapter == null || !_adapter.IsEnabled)
            {
                System.Diagnostics.Debug.WriteLine("Bluetooth adapter missing or disabled.");
                return false;
            }

            if (!_adapter.IsMultipleAdvertisementSupported)
            {
                System.Diagnostics.Debug.WriteLine("BLE advertising not supported.");
                return false;
            }

            // Create GATT Server
            _server = _mgr.OpenGattServer(_ctx, this);
            var service = new BluetoothGattService(UUID_HRS, GattServiceType.Primary);

            // Heart Rate Measurement characteristic
            _hrChar = new BluetoothGattCharacteristic(
                UUID_HR_MEAS,
                GattProperty.Notify,
                GattPermission.Read
            );

            // CCC descriptor
            var ccc = new BluetoothGattDescriptor(UUID_CCC,
                GattDescriptorPermission.Read | GattDescriptorPermission.Write);
            _hrChar.AddDescriptor(ccc);

            // Body location (e.g., chest)
            var bodyLoc = new BluetoothGattCharacteristic(
                UUID_BODY_LOC,
                GattProperty.Read,
                GattPermission.Read);
            bodyLoc.SetValue(new byte[] { 1 }); // Chest

            // Add to service
            service.AddCharacteristic(_hrChar);
            service.AddCharacteristic(bodyLoc);
            _server.AddService(service);

            // Configure advertiser
            _adv = _adapter.BluetoothLeAdvertiser;
            var data = new AdvertiseData.Builder()
                .AddServiceUuid(new ParcelUuid(UUID_HRS))
                .SetIncludeDeviceName(true)
                .Build();

            var settings = new AdvertiseSettings.Builder()
                .SetAdvertiseMode(AdvertiseMode.LowLatency)
                .SetTxPowerLevel(AdvertiseTx.PowerHigh)
                .SetConnectable(true)
                .Build();

            _adv.StartAdvertising(settings, data, _callback);
            System.Diagnostics.Debug.WriteLine("BLE advertising started successfully.");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BLE start error: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Stop advertising and close the GATT server.
    /// </summary>
    public void StopAdvertising()
    {
        try { _adv?.StopAdvertising(_callback); } catch { }
        try { _server?.Close(); } catch { }
        System.Diagnostics.Debug.WriteLine("BLE advertising stopped.");
    }

    /// <summary>
    /// Push updated BPM to subscribed clients.
    /// </summary>
    public void UpdateHeartRate(int bpm)
    {
        if (_server == null || _subscribed == null || _hrChar == null)
            return;

        // Flags = 0x00 (UINT8 BPM)
        byte flags = 0x00;
        byte hb = (byte)Math.Max(0, Math.Min(255, bpm));

        var payload = new byte[] { flags, hb };
        _hrChar.SetValue(payload);

        try
        {
            _server.NotifyCharacteristicChanged(_subscribed, _hrChar, false);
            System.Diagnostics.Debug.WriteLine($"Sent HR notification: {bpm} bpm");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HR notify error: {ex}");
        }
    }

    // -----------------------------------------------------------------------
    // GATT CALLBACKS
    // -----------------------------------------------------------------------

    public override void OnConnectionStateChange(BluetoothDevice device, ProfileState status, ProfileState newState)
    {
        base.OnConnectionStateChange(device, status, newState);

        if (newState == ProfileState.Connected)
        {
            _subscribed = device;
            System.Diagnostics.Debug.WriteLine($"Device connected: {device.Address}");
        }
        else if (newState == ProfileState.Disconnected && _subscribed == device)
        {
            _subscribed = null;
            System.Diagnostics.Debug.WriteLine($"Device disconnected: {device.Address}");
        }
    }

    public override void OnDescriptorWriteRequest(BluetoothDevice device, int requestId, BluetoothGattDescriptor descriptor,
        bool preparedWrite, bool responseNeeded, int offset, byte[] value)
    {
        base.OnDescriptorWriteRequest(device, requestId, descriptor, preparedWrite, responseNeeded, offset, value);

        if (descriptor.Uuid.Equals(UUID_CCC))
        {
            _server?.SendResponse(device, requestId, GattStatus.Success, 0, value);
            var enabled = value != null && value.Length >= 2 && value[0] == 0x01 && value[1] == 0x00;
            if (enabled)
            {
                _subscribed = device;
                System.Diagnostics.Debug.WriteLine("Notifications enabled.");
            }
            else if (_subscribed == device)
            {
                _subscribed = null;
                System.Diagnostics.Debug.WriteLine("Notifications disabled.");
            }
        }
    }

    // --- Compatibility shims for older call sites ---
    public bool StartAsync()
        => StartAdvertising();

    public void Stop()
        => StopAdvertising();

    public void NotifyHeartRate(byte bpm)
        => UpdateHeartRate(bpm);

    // -----------------------------------------------------------------------
    // INTERNAL ADVERTISING CALLBACK
    // -----------------------------------------------------------------------
    private class AdvCb : AdvertiseCallback
    {
        public override void OnStartFailure(AdvertiseFailure errorCode)
        {
            System.Diagnostics.Debug.WriteLine($"Advertising failed: {errorCode}");
        }

        public override void OnStartSuccess(AdvertiseSettings settingsInEffect)
        {
            System.Diagnostics.Debug.WriteLine("Advertising success!");
        }
    }
}