using System;
using System.Collections.Generic;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.OS;
using Android.Runtime;
using Android.Util;

using ScanModeLE = Android.Bluetooth.LE.ScanMode;

namespace HRPeripheral;

public sealed class BleScanner : ScanCallback
{
    public const string Tag = "BLE";

    private readonly BluetoothLeScanner? _scanner;
    private readonly BluetoothAdapter? _adapter;

    // raise this when a matching device is found
    public event Action<BluetoothDevice>? DeviceFound;

    private static readonly ParcelUuid HeartRate =
        ParcelUuid.FromString("0000180D-0000-1000-8000-00805F9B34FB");

    public BleScanner()
    {
        _adapter = BluetoothAdapter.DefaultAdapter;
        _scanner = _adapter?.BluetoothLeScanner;
    }

    public bool Start()
    {
        if (_adapter == null || _scanner == null)
        {
            Log.Warn(Tag, "Bluetooth adapter/scanner not available");
            return false;
        }
        if (!_adapter.IsEnabled)
        {
            Log.Warn(Tag, "Bluetooth is OFF");
            return false;
        }

        var filters = new List<ScanFilter>
        {
            new ScanFilter.Builder()
                .SetServiceUuid(HeartRate)   // filter to Heart Rate service
                .Build()
        };

        var settings = new ScanSettings.Builder()
            .SetScanMode(ScanModeLE.LowLatency)
            .Build();

        Log.Info(Tag, "Starting BLE scan…");
        _scanner.StartScan(filters, settings, this);
        return true;
    }

    public void Stop()
    {
        if (_scanner == null) return;
        Log.Info(Tag, "Stopping BLE scan");
        _scanner.StopScan(this);
    }

    public override void OnScanResult([GeneratedEnum] ScanCallbackType callbackType, ScanResult? result)
    {
        if (result?.Device == null) return;

        var device = result.Device;
        var name = device.Name ?? "(no name)";
        var addr = device.Address;
        var rssi = result.Rssi;

        Log.Info(Tag, $"Found: {name} [{addr}] RSSI={rssi}");

        // fire event so MainActivity can connect
        DeviceFound?.Invoke(device);
    }

    public override void OnBatchScanResults(IList<ScanResult>? results)
    {
        if (results == null) return;
        foreach (var r in results)
            OnScanResult(ScanCallbackType.AllMatches, r);
    }

    public override void OnScanFailed([GeneratedEnum] ScanFailure errorCode)
        => Log.Error(Tag, $"Scan failed: {errorCode}");
}