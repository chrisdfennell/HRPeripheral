using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using System;
// Explicit aliases to avoid ambiguous types
using SysDebug = System.Diagnostics.Debug;
using SysException = System.Exception;

// Add using aliases to resolve ambiguity between .NET and Android types.
using Activity = Android.App.Activity;

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
    private HrUpdateReceiver? _updateReceiver;

    // Force-quit press & hold
    private View? _exitOverlay;
    private long _holdStart;
    private bool _holding;
    private readonly long _holdMillis = 10_000L; // 10 seconds
    private readonly Handler _handler = new Handler(Looper.MainLooper);
    private Java.Lang.IRunnable? _triggerExit;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

        _hrText = FindViewById<TextView>(Resource.Id.hr_value);
        _exitOverlay = FindViewById(Resource.Id.exitHoldOverlay); // full-screen invisible overlay

        if (!BlePermissions.HasAll(this))
        {
            BlePermissions.Request(this);
        }
        else
        {
            StartHrService();
        }

        // Long-hold overlay setup
        _triggerExit = new Java.Lang.Runnable(() =>
        {
            if (_holding && (Java.Lang.JavaSystem.CurrentTimeMillis() - _holdStart) >= _holdMillis)
            {
                Haptic(longBuzz: true);
                ForceCloseApp();
            }
        });

        if (_exitOverlay != null)
        {
            _exitOverlay.Touch += (s, e) =>
            {
                switch (e.Event.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _holdStart = Java.Lang.JavaSystem.CurrentTimeMillis();
                        _holding = true;
                        Haptic();
                        ScheduleCountdownHaptics();
                        _handler.PostDelayed(_triggerExit, _holdMillis);
                        e.Handled = true;
                        break;

                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        CancelHold();
                        e.Handled = true;
                        break;

                    default:
                        e.Handled = false;
                        break;
                }
            };
        }
    }

    private void CancelHold()
    {
        if (!_holding) return;
        _holding = false;
        _handler.RemoveCallbacks(_triggerExit);
        _handler.RemoveCallbacksAndMessages(null);
        Toast.MakeText(this, "Hold cancelled", ToastLength.Short).Show();
    }

    private void ScheduleCountdownHaptics()
    {
        // Optional: short tick each second
        for (int i = 1; i <= 9; i++)
        {
            int delay = i * 1000;
            _handler.PostDelayed(new Java.Lang.Runnable(() =>
            {
                if (_holding) Haptic();
            }), delay);
        }
    }

    private void Haptic(bool longBuzz = false)
    {
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S) // 31+
            {
                var vm = (VibratorManager)GetSystemService(VibratorManagerService);
                var vib = vm.DefaultVibrator;
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                    vib.Vibrate(VibrationEffect.CreateOneShot(longBuzz ? 180 : 30, VibrationEffect.DefaultAmplitude));
                else
#pragma warning disable CA1416
                    vib.Vibrate(longBuzz ? 180 : 30);
#pragma warning restore CA1416
            }
            else
            {
                var vib = (Vibrator)GetSystemService(VibratorService);
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                    vib.Vibrate(VibrationEffect.CreateOneShot(longBuzz ? 180 : 30, VibrationEffect.DefaultAmplitude));
                else
#pragma warning disable CA1416
                    vib.Vibrate(longBuzz ? 180 : 30);
#pragma warning restore CA1416
            }
        }
        catch { /* best-effort */ }
    }

    private void ForceCloseApp()
    {
        try
        {
            // Stop service cleanly
            StopService(new Intent(this, typeof(HeartRateService)));

            // Close activity/task
            FinishAndRemoveTask();

            // Optional hard kill (not recommended):
            // Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
            // Environment.Exit(0);
        }
        catch (SysException ex)
        {
            SysDebug.WriteLine($"ForceCloseApp error: {ex}");
        }
    }

    private void StartHrService()
    {
        var serviceIntent = new Intent(this, typeof(HeartRateService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            StartForegroundService(serviceIntent);
        }
        else
        {
            StartService(serviceIntent);
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
            Toast.MakeText(this, "Permissions are required for the app to function.", ToastLength.Long).Show();
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Replace '??=' (preview) with classic null-check:
        if (_updateReceiver == null)
            _updateReceiver = new HrUpdateReceiver(_hrText);

        var filter = new IntentFilter(HeartRateService.ACTION_UPDATE);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            RegisterReceiver(_updateReceiver, filter, ReceiverFlags.NotExported);
        }
        else
        {
            RegisterReceiver(_updateReceiver, filter);
        }
    }

    protected override void OnPause()
    {
        if (_updateReceiver != null)
        {
            try
            {
                UnregisterReceiver(_updateReceiver);
            }
            catch (Java.Lang.IllegalArgumentException)
            {
                SysDebug.WriteLine("Receiver was not registered, skipping unregister.");
            }
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
                SysDebug.WriteLine($"HrUpdateReceiver: Received HR update: {hr} bpm");

                if (_targetView != null && hr > 0)
                {
                    _targetView.Text = $"{hr} bpm";
                }
            }
        }
    }
}