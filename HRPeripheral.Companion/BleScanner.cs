using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.OS;
using Java.Util;
using System.Diagnostics;
using Debug = System.Diagnostics.Debug;

namespace HRPeripheral.Companion;

/// <summary>
/// BLE scanner that discovers peripherals advertising the Heart Rate Service (0x180D).
/// </summary>
public class BleScanner
{
    private static readonly UUID UUID_HR_SERVICE = UUID.FromString("0000180D-0000-1000-8000-00805f9b34fb")!;

    private BluetoothLeScanner? _scanner;
    private ScanCallbackImpl? _scanCallback;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private const int SCAN_TIMEOUT_MS = 15_000;

    public event Action<BluetoothDevice>? OnDeviceFound;
    public event Action? OnScanStopped;

    public void StartScan(BluetoothAdapter adapter)
    {
        _scanner = adapter.BluetoothLeScanner;
        if (_scanner == null)
        {
            Debug.WriteLine("BLE scanner not available.");
            return;
        }

        _scanCallback = new ScanCallbackImpl(this);

        var filter = new ScanFilter.Builder()!
            .SetServiceUuid(new ParcelUuid(UUID_HR_SERVICE))!
            .Build()!;

        var settings = new ScanSettings.Builder()!
            .SetScanMode(Android.Bluetooth.LE.ScanMode.LowLatency)!
            .Build()!;

        _scanner.StartScan(new List<ScanFilter> { filter }, settings, _scanCallback);
        Debug.WriteLine("BLE scan started.");

        // Auto-stop after timeout
        _handler.PostDelayed(() => StopScan(), SCAN_TIMEOUT_MS);
    }

    public void StopScan()
    {
        try
        {
            if (_scanCallback != null)
                _scanner?.StopScan(_scanCallback);
        }
        catch (Exception ex) { Debug.WriteLine($"StopScan error: {ex.Message}"); }

        _handler.RemoveCallbacksAndMessages(null);
        OnScanStopped?.Invoke();
        Debug.WriteLine("BLE scan stopped.");
    }

    private class ScanCallbackImpl : ScanCallback
    {
        private readonly BleScanner _parent;
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

        public ScanCallbackImpl(BleScanner parent) => _parent = parent;

        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            base.OnScanResult(callbackType, result);
            if (result?.Device?.Address == null) return;

            if (_seen.Add(result.Device.Address))
            {
                Debug.WriteLine($"Discovered HR device: {result.Device.Address} ({result.Device.Name})");
                _parent.OnDeviceFound?.Invoke(result.Device);
            }
        }

        public override void OnScanFailed(ScanFailure errorCode)
        {
            base.OnScanFailed(errorCode);
            Debug.WriteLine($"BLE scan failed: {errorCode}");
        }
    }
}
