using Android;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Android.Content.PM;
using Android.App;
using System.Collections.Generic;

namespace HRPeripheral;

public static class BlePermissions
{
    public const int RequestCode = 1001;

    public static string[] RequiredPermissions
    {
        get
        {
            var permissions = new List<string>();

            // Bluetooth permissions for Android 12 (S) and above
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                permissions.Add(Manifest.Permission.BluetoothScan);
                permissions.Add(Manifest.Permission.BluetoothConnect);
                permissions.Add(Manifest.Permission.BluetoothAdvertise);
            }
            // Location permission for older versions (required for BLE scanning)
            else
            {
                permissions.Add(Manifest.Permission.AccessFineLocation);
            }

            // Body sensor permission (for heart rate) is always needed
            permissions.Add(Manifest.Permission.BodySensors);

            // Foreground service type permission for Android 14 (U) and above
            if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
            {
                permissions.Add(Manifest.Permission.ForegroundServiceHealth);
            }

            return permissions.ToArray();
        }
    }

    public static bool HasAll(Activity activity)
    {
        foreach (var p in RequiredPermissions)
            if (ContextCompat.CheckSelfPermission(activity, p) != (int)Permission.Granted)
                return false;
        return true;
    }

    public static void Request(Activity activity)
    {
        ActivityCompat.RequestPermissions(activity, RequiredPermissions, RequestCode);
    }
}