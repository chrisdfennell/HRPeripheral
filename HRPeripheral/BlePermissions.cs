using Android;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Android.Content.PM;
using Android.App;

namespace HRPeripheral;

public static class BlePermissions
{
    public const int RequestCode = 1001;

    public static string[] RequiredPermissions =>
        Build.VERSION.SdkInt >= BuildVersionCodes.S
        ? new[] {
            Manifest.Permission.BluetoothScan,
            Manifest.Permission.BluetoothConnect,
        }
        : new[] {
            Manifest.Permission.AccessFineLocation, // needed for BLE scan pre-12
        };

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