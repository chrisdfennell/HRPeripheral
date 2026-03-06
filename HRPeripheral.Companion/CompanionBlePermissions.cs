using Android.App;
using Android.Content.PM;
using Android.OS;

namespace HRPeripheral.Companion;

/// <summary>
/// BLE permission helpers for the companion phone app (Central role).
/// Does NOT need BLUETOOTH_ADVERTISE or BODY_SENSORS.
/// </summary>
public static class CompanionBlePermissions
{
    public const int RequestCode = 2001;

    public static string[] RequiredPermissions
    {
        get
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S) // Android 12+
            {
                return new[]
                {
                    Android.Manifest.Permission.BluetoothScan,
                    Android.Manifest.Permission.BluetoothConnect,
                };
            }
            return new[]
            {
                Android.Manifest.Permission.AccessFineLocation,
            };
        }
    }

    public static bool HasAll(Activity activity)
    {
        foreach (var p in RequiredPermissions)
        {
            if (activity.CheckSelfPermission(p) != Permission.Granted)
                return false;
        }
        return true;
    }

    public static void Request(Activity activity)
    {
        activity.RequestPermissions(RequiredPermissions, RequestCode);
    }
}
