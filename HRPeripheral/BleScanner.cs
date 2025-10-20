using System;
using System.Collections.Generic;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.OS;
using Android.Runtime;
using Android.Util;

// Alias to disambiguate Android’s LE ScanMode enum from other possible references
using ScanModeLE = Android.Bluetooth.LE.ScanMode;

namespace HRPeripheral;

/// <summary>
/// Handles Bluetooth Low Energy (BLE) scanning for nearby devices
/// that advertise the *Heart Rate Service* (UUID 0x180D).
///
/// This class wraps Android’s <see cref="BluetoothLeScanner"/> API,
/// provides simple Start/Stop methods, and raises an event when a
/// matching device is discovered.  
///
/// Usage:
///   • Create once (e.g. in MainActivity).
///   • Subscribe to <see cref="DeviceFound"/> to get discovered devices.
///   • Call <see cref="Start"/> to begin scanning, <see cref="Stop"/> to stop.
/// </summary>
public sealed class BleScanner : ScanCallback
{
    // Tag used for Android log output
    public const string Tag = "BLE";

    // References to the system BLE scanner and adapter
    private readonly BluetoothLeScanner? _scanner;
    private readonly BluetoothAdapter? _adapter;

    /// <summary>
    /// Event fired whenever a matching device is found during scanning.
    /// The MainActivity subscribes to this event to initiate a connection.
    /// </summary>
    public event Action<BluetoothDevice>? DeviceFound;

    /// <summary>
    /// UUID filter: the standard Heart Rate Service (0x180D).
    /// We only care about peripherals advertising this service.
    /// </summary>
    private static readonly ParcelUuid HeartRate =
        ParcelUuid.FromString("0000180D-0000-1000-8000-00805F9B34FB");

    /// <summary>
    /// Constructor — sets up Bluetooth adapter and scanner references.
    /// </summary>
    public BleScanner()
    {
        _adapter = BluetoothAdapter.DefaultAdapter;     // system-wide Bluetooth entry point
        _scanner = _adapter?.BluetoothLeScanner;        // high-level BLE scanning interface
    }

    /// <summary>
    /// Starts scanning for BLE devices that advertise the Heart Rate Service.
    /// Returns false if the adapter or scanner is unavailable or Bluetooth is off.
    /// </summary>
    public bool Start()
    {
        // Check if Bluetooth hardware or scanner is missing
        if (_adapter == null || _scanner == null)
        {
            Log.Warn(Tag, "Bluetooth adapter/scanner not available");
            return false;
        }

        // Make sure Bluetooth is turned on before scanning
        if (!_adapter.IsEnabled)
        {
            Log.Warn(Tag, "Bluetooth is OFF");
            return false;
        }

        // Build scan filter:
        // This limits scan results to devices advertising the Heart Rate Service UUID.
        var filters = new List<ScanFilter>
        {
            new ScanFilter.Builder()
                .SetServiceUuid(HeartRate)   // target Heart Rate Service (0x180D)
                .Build()
        };

        // Build scan settings:
        //   • LowLatency = fastest scan rate (more power usage, better responsiveness)
        //   • Ideal for short bursts or user-triggered scans
        var settings = new ScanSettings.Builder()
            .SetScanMode(ScanModeLE.LowLatency)
            .Build();

        // Start scanning
        Log.Info(Tag, "Starting BLE scan…");
        _scanner.StartScan(filters, settings, this);

        return true;
    }

    /// <summary>
    /// Stops any ongoing BLE scan.
    /// Safe to call multiple times.
    /// </summary>
    public void Stop()
    {
        if (_scanner == null)
            return;

        Log.Info(Tag, "Stopping BLE scan");
        _scanner.StopScan(this);
    }

    // ========================================================================
    // BLE CALLBACKS (inherited from Android.Bluetooth.LE.ScanCallback)
    // ========================================================================

    /// <summary>
    /// Called whenever a single device result is found that matches our filters.
    /// This callback runs frequently as new devices are discovered or their
    /// advertisement data changes.
    /// </summary>
    public override void OnScanResult([GeneratedEnum] ScanCallbackType callbackType, ScanResult? result)
    {
        // Defensive: null checks to avoid NREs
        if (result?.Device == null)
            return;

        var device = result.Device;
        var name = device.Name ?? "(no name)";
        var addr = device.Address;
        var rssi = result.Rssi;

        // Log details to Android Logcat for debugging
        Log.Info(Tag, $"Found: {name} [{addr}] RSSI={rssi}");

        // Raise DeviceFound event so MainActivity (or other listeners)
        // can connect or display the device in a list.
        DeviceFound?.Invoke(device);
    }

    /// <summary>
    /// Called when multiple results are reported in a batch (less frequent).
    /// We simply forward each to OnScanResult for consistency.
    /// </summary>
    public override void OnBatchScanResults(IList<ScanResult>? results)
    {
        if (results == null)
            return;

        foreach (var r in results)
            OnScanResult(ScanCallbackType.AllMatches, r);
    }

    /// <summary>
    /// Called if scanning fails to start or is aborted unexpectedly.
    /// Logs the error for debugging.
    /// </summary>
    public override void OnScanFailed([GeneratedEnum] ScanFailure errorCode)
        => Log.Error(Tag, $"Scan failed: {errorCode}");
}
