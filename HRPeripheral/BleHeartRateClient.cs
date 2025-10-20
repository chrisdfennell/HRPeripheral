using Android.Bluetooth;
using Android.Content;
using Android.Runtime;
using Java.Util;
using System;
using System.Linq;

namespace HRPeripheral;

/// <summary>
/// Handles Bluetooth Low Energy (BLE) connection and data exchange
/// for heart rate measurement devices using the standard GATT Heart Rate Service (UUID 0x180D).
/// 
/// Responsibilities:
///  - Connect to a BLE heart rate peripheral.
///  - Discover its services and subscribe to heart rate measurement notifications.
///  - Parse the received heart rate data and raise events for the UI.
/// </summary>
public class BleHeartRateClient : BluetoothGattCallback
{
    // --- UUIDs for the official BLE Heart Rate Service ---
    // Standard BLE UUIDs are well-defined in the Bluetooth SIG specifications.
    static readonly UUID HR_SERVICE = UUID.FromString("0000180D-0000-1000-8000-00805F9B34FB");         // Heart Rate Service
    static readonly UUID HR_MEASUREMENT_CHAR = UUID.FromString("00002A37-0000-1000-8000-00805F9B34FB"); // Heart Rate Measurement Characteristic
    static readonly UUID CCCD = UUID.FromString("00002902-0000-1000-8000-00805F9B34FB");                // Client Characteristic Configuration Descriptor

    // Active GATT connection reference (used to manage communication with the BLE device)
    BluetoothGatt? _gatt;

    // Context used for Bluetooth connection operations
    readonly Context _ctx;

    // --- EVENTS ---

    /// <summary>
    /// Raised when the heart rate (in BPM) changes — used by UI or logic layers.
    /// </summary>
    public event Action<int>? HeartRateChanged;

    /// <summary>
    /// Raised when a log-worthy message occurs (e.g., status, errors, connection info).
    /// </summary>
    public event Action<string>? Log;

    /// <summary>
    /// Constructor that stores context for use when connecting to a device.
    /// </summary>
    /// <param name="ctx">Android context (usually an Activity or Service)</param>
    public BleHeartRateClient(Context ctx) => _ctx = ctx;

    // --- CONNECTION HANDLING ---

    /// <summary>
    /// Connect to a specific BLE device.
    /// If already connected, first disconnects the previous connection.
    /// </summary>
    /// <param name="device">Bluetooth device to connect to.</param>
    public void Connect(BluetoothDevice device)
    {
        // Ensure previous GATT is closed before starting a new one
        Disconnect();

        Log?.Invoke($"Connecting to {device.Name ?? device.Address}…");

        // Initiate a new GATT connection (autoConnect = false for direct connection)
        _gatt = device.ConnectGatt(_ctx, autoConnect: false, callback: this);
    }

    /// <summary>
    /// Cleanly closes the current GATT connection.
    /// </summary>
    public void Disconnect()
    {
        try
        {
            _gatt?.Close();
        }
        catch
        {
            // Safe cleanup — ignore exceptions from invalid state
        }

        _gatt = null;
    }

    // --- GATT CALLBACK OVERRIDES ---

    /// <summary>
    /// Called when the BLE connection state changes.
    /// </summary>
    /// <param name="gatt">The GATT client</param>
    /// <param name="status">Status of the operation (e.g., Success, Failure)</param>
    /// <param name="newState">The new connection state (Connected, Disconnected, etc.)</param>
    public override void OnConnectionStateChange(BluetoothGatt gatt, [GeneratedEnum] GattStatus status, [GeneratedEnum] ProfileState newState)
    {
        if (status != GattStatus.Success)
        {
            // Connection failed or dropped
            Log?.Invoke($"GATT error: {status}");
            gatt.Close();
            return;
        }

        if (newState == ProfileState.Connected)
        {
            // Connected successfully, start discovering available services
            Log?.Invoke("Connected. Discovering services…");
            gatt.DiscoverServices();
        }
        else if (newState == ProfileState.Disconnected)
        {
            // Remote device disconnected
            Log?.Invoke("Disconnected.");
        }
    }

    /// <summary>
    /// Called after services have been discovered.
    /// Here we locate the Heart Rate Service and enable notifications.
    /// </summary>
    public override void OnServicesDiscovered(BluetoothGatt gatt, [GeneratedEnum] GattStatus status)
    {
        if (status != GattStatus.Success)
        {
            Log?.Invoke($"Service discovery failed: {status}");
            return;
        }

        // Try to get the Heart Rate service
        var svc = gatt.GetService(HR_SERVICE);
        var ch = svc?.GetCharacteristic(HR_MEASUREMENT_CHAR);
        if (ch == null)
        {
            Log?.Invoke("Heart Rate characteristic not found.");
            return;
        }

        // --- ENABLE NOTIFICATIONS ---
        // Heart rate data is sent as notifications, so we must explicitly enable them.
        gatt.SetCharacteristicNotification(ch, true);

        // Every characteristic that supports notifications has a CCCD descriptor.
        var cccd = ch.GetDescriptor(CCCD);
        if (cccd != null)
        {
            // Write to the CCCD to enable notifications on the device side
            cccd.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
            gatt.WriteDescriptor(cccd);
            Log?.Invoke("Enabled HR notifications.");
        }
        else
        {
            Log?.Invoke("Missing CCCD; cannot enable notifications.");
        }
    }

    /// <summary>
    /// Called whenever the connected device sends updated characteristic data.
    /// </summary>
    /// <param name="gatt">The GATT client</param>
    /// <param name="characteristic">The characteristic that changed</param>
    public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
    {
        // Only handle the Heart Rate Measurement characteristic
        if (characteristic.Uuid.Equals(HR_MEASUREMENT_CHAR))
        {
            var data = characteristic.GetValue();
            if (data == null || data.Length == 0) return;

            // Heart Rate Measurement format (defined in the Bluetooth SIG spec)
            // Byte 0 = Flags
            //   bit0 = 1 means Heart Rate value format is UINT16 (2 bytes), else UINT8 (1 byte)
            // Byte 1..2 = Heart Rate Measurement Value (in BPM)

            bool hr16 = (data[0] & 0x01) != 0;
            int bpm = hr16 ? (data[1] | (data[2] << 8)) : data[1];

            // Raise event for UI or logic subscribers
            HeartRateChanged?.Invoke(bpm);
        }
    }
}