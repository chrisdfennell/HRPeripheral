using Android;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Android.Content.PM;
using Android.App;
using System.Collections.Generic;

namespace HRPeripheral;

/// <summary>
/// Provides a centralized helper for requesting and checking Bluetooth-related permissions
/// required by the HRPeripheral app across various Android API levels.
///
/// This class abstracts away Android’s evolving BLE permission model:
///   - Android 6–11 → Location permissions required for BLE scanning.
///   - Android 12+ (API 31 / S) → Explicit Bluetooth permissions introduced.
///   - Android 14+ (API 34 / U) → ForegroundServiceHealth added for health data access.
///
/// By consolidating these checks and requests in one place, we avoid duplicating
/// fragile permission logic throughout Activities.
/// </summary>
public static class BlePermissions
{
    /// <summary>
    /// Arbitrary request code used when calling ActivityCompat.RequestPermissions().
    /// Used to identify BLE permission requests in OnRequestPermissionsResult.
    /// </summary>
    public const int RequestCode = 1001;

    /// <summary>
    /// Dynamically computes the list of permissions required at runtime
    /// depending on the Android API level.
    /// </summary>
    public static string[] RequiredPermissions
    {
        get
        {
            var permissions = new List<string>();

            // ===============================================================
            // ANDROID 12 (S / API 31) AND ABOVE
            // ===============================================================
            // As of Android 12, Bluetooth operations were decoupled from location.
            // Apps now need these *specific* permissions instead of ACCESS_FINE_LOCATION:
            //   • BLUETOOTH_SCAN – to discover BLE devices
            //   • BLUETOOTH_CONNECT – to connect to known devices
            //   • BLUETOOTH_ADVERTISE – to act as a BLE peripheral (like we do)
            // Each must also declare <uses-permission> in AndroidManifest.xml.
            // ===============================================================
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                permissions.Add(Manifest.Permission.BluetoothScan);
                permissions.Add(Manifest.Permission.BluetoothConnect);
                permissions.Add(Manifest.Permission.BluetoothAdvertise);
            }
            // ===============================================================
            // ANDROID 6.0 – 11 (API 23–30)
            // ===============================================================
            // Before Android 12, Bluetooth scanning implicitly required coarse/fine
            // location access because BLE scans could reveal nearby devices.
            // ACCESS_FINE_LOCATION was therefore mandatory for BLE operations.
            // ===============================================================
            else
            {
                permissions.Add(Manifest.Permission.AccessFineLocation);
            }

            // ===============================================================
            // BODY SENSORS
            // ===============================================================
            // Always required when reading or broadcasting heart rate data,
            // even if the app acts as a BLE *peripheral* rather than a *sensor*.
            // Protects access to physiological data (Heart Rate, ECG, etc.)
            // ===============================================================
            permissions.Add(Manifest.Permission.BodySensors);

            // ===============================================================
            // ANDROID 14 (UpsideDownCake / API 34) AND ABOVE
            // ===============================================================
            // As of Android 14, apps using Foreground Services to process
            // health-related BLE data (like heart rate) must declare an additional
            // permission to use the "health" foreground service type.
            //
            // This permission ensures the app remains visible while performing
            // continuous heart rate streaming.
            // ===============================================================
            if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
            {
                permissions.Add(Manifest.Permission.ForegroundServiceHealth);
            }

            // Return as array for use in RequestPermissions().
            return permissions.ToArray();
        }
    }

    /// <summary>
    /// Checks whether the app already has *all* required permissions granted.
    /// </summary>
    /// <param name="activity">The current Activity (context for permission check).</param>
    /// <returns>True if all permissions are granted, otherwise false.</returns>
    public static bool HasAll(Activity activity)
    {
        foreach (var p in RequiredPermissions)
        {
            // ContextCompat handles the runtime permission compatibility layer.
            // If *any* permission is not granted, we return false immediately.
            if (ContextCompat.CheckSelfPermission(activity, p) != (int)Permission.Granted)
                return false;
        }

        // All checked permissions are granted.
        return true;
    }

    /// <summary>
    /// Requests all necessary BLE and health permissions at once.
    /// This will trigger the standard Android permission dialog.
    /// </summary>
    /// <param name="activity">The Activity initiating the request.</param>
    public static void Request(Activity activity)
    {
        // Delegates to AndroidX ActivityCompat, which handles both legacy and modern flows.
        // This can be safely called on any Android version.
        ActivityCompat.RequestPermissions(activity, RequiredPermissions, RequestCode);
    }
}