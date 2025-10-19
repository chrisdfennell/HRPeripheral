using Android.Bluetooth;
using Android.Content;
using Android.Runtime;
using Java.Util;
using System;
using System.Linq;

namespace HRPeripheral;

public class BleHeartRateClient : BluetoothGattCallback
{
    static readonly UUID HR_SERVICE = UUID.FromString("0000180D-0000-1000-8000-00805F9B34FB");
    static readonly UUID HR_MEASUREMENT_CHAR = UUID.FromString("00002A37-0000-1000-8000-00805F9B34FB");
    static readonly UUID CCCD = UUID.FromString("00002902-0000-1000-8000-00805F9B34FB");

    BluetoothGatt? _gatt;
    readonly Context _ctx;

    public event Action<int>? HeartRateChanged;
    public event Action<string>? Log;

    public BleHeartRateClient(Context ctx) => _ctx = ctx;

    public void Connect(BluetoothDevice device)
    {
        Disconnect();
        Log?.Invoke($"Connecting to {device.Name ?? device.Address}…");
        _gatt = device.ConnectGatt(_ctx, autoConnect: false, callback: this);
    }

    public void Disconnect()
    {
        try { _gatt?.Close(); } catch { }
        _gatt = null;
    }

    public override void OnConnectionStateChange(BluetoothGatt gatt, [GeneratedEnum] GattStatus status, [GeneratedEnum] ProfileState newState)
    {
        if (status != GattStatus.Success)
        {
            Log?.Invoke($"GATT error: {status}");
            gatt.Close(); return;
        }

        if (newState == ProfileState.Connected)
        {
            Log?.Invoke("Connected. Discovering services…");
            gatt.DiscoverServices();
        }
        else if (newState == ProfileState.Disconnected)
        {
            Log?.Invoke("Disconnected.");
        }
    }

    public override void OnServicesDiscovered(BluetoothGatt gatt, [GeneratedEnum] GattStatus status)
    {
        if (status != GattStatus.Success) { Log?.Invoke($"Service discovery failed: {status}"); return; }

        var svc = gatt.GetService(HR_SERVICE);
        var ch = svc?.GetCharacteristic(HR_MEASUREMENT_CHAR);
        if (ch == null) { Log?.Invoke("Heart Rate characteristic not found."); return; }

        // Enable notifications
        gatt.SetCharacteristicNotification(ch, true);
        var cccd = ch.GetDescriptor(CCCD);
        if (cccd != null)
        {
            cccd.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
            gatt.WriteDescriptor(cccd);
            Log?.Invoke("Enabled HR notifications.");
        }
        else
        {
            Log?.Invoke("Missing CCCD; cannot enable notifications.");
        }
    }

    public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
    {
        if (characteristic.Uuid.Equals(HR_MEASUREMENT_CHAR))
        {
            var data = characteristic.GetValue();
            if (data == null || data.Length == 0) return;

            // Heart Rate Measurement format (flags in byte0)
            // if (flags & 0x01) bpm is uint16 in byte1..2 else uint8 in byte1
            int bpm;
            bool hr16 = (data[0] & 0x01) != 0;
            bpm = hr16 ? (data[1] | (data[2] << 8)) : data[1];

            HeartRateChanged?.Invoke(bpm);
        }
    }
}