using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Widget;

namespace HRPeripheral;

[Activity(
    Label = "HR Peripheral",
    MainLauncher = true,
    Theme = "@android:style/Theme.DeviceDefault",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize
)]
public class MainActivity : Activity
{
    private TextView? _hrText;
    private BroadcastReceiver? _updateReceiver;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        _hrText = FindViewById<TextView>(Resource.Id.hr_value);

        if (!BlePermissions.HasAll(this))
        {
            BlePermissions.Request(this);
        }
        else
        {
            StartHrService();
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == BlePermissions.RequestCode && BlePermissions.HasAll(this))
        {
            StartHrService();
        }
        else
        {
            Toast.MakeText(this, "Permissions required to function.", ToastLength.Long).Show();
        }
    }

    private void StartHrService()
    {
        var intent = new Intent(this, typeof(HeartRateService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            StartForegroundService(intent);
        else
            StartService(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Prepare receiver for service updates
        _updateReceiver ??= new HrUpdateReceiver(_hrText);

        var filter = new IntentFilter(HeartRateService.ACTION_UPDATE);

        // Android 13+ requires specifying exported/not-exported for dynamic receivers
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            RegisterReceiver(_updateReceiver, filter, ReceiverFlags.NotExported);
        else
            RegisterReceiver(_updateReceiver, filter);
    }

    protected override void OnPause()
    {
        // Always unregister to avoid leaks/crashes on resume
        if (_updateReceiver != null)
        {
            try { UnregisterReceiver(_updateReceiver); }
            catch { /* ignore if not registered */ }
        }
        base.OnPause();
    }

    /// <summary>
    /// Receives heart-rate updates from HeartRateService and updates the UI.
    /// </summary>
    private class HrUpdateReceiver : BroadcastReceiver
    {
        private readonly TextView? _targetView;

        public HrUpdateReceiver(TextView? targetView) => _targetView = targetView;

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == HeartRateService.ACTION_UPDATE)
            {
                int hr = intent.GetIntExtra("hr", 0);
                if (_targetView != null)
                    _targetView.Text = $"{hr} bpm";
            }
        }
    }
}