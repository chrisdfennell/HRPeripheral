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
// Xamarin.Android activity alias
using Activity = Android.App.Activity;

using HRPeripheral.Views; // HoldCountdownView

namespace HRPeripheral;

[Activity(
    Label = "HR Peripheral",
    MainLauncher = true,
    Theme = "@android:style/Theme.DeviceDefault",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize
)]
public class MainActivity : Activity
{
    // SharedPreferences
    private const string PREFS = "hrp_prefs";
    private const string PREF_HOLD_ENABLED = "hold_enabled";
    private const string PREF_HOLD_SECONDS = "hold_seconds"; // stored as offset 0..10 (maps to 5..15s)

    private TextView? _hrText;
    private HrUpdateReceiver? _updateReceiver;

    // Press & hold to exit
    private View? _exitOverlay;
    private HoldCountdownView? _countdown;
    private long _holdStart;
    private bool _holding;
    private long _holdMillis = 10_000L; // will be overwritten by prefs
    private readonly Handler _handler = new Handler(Looper.MainLooper);
    private Java.Lang.IRunnable? _triggerExit;
    private Java.Lang.IRunnable? _progressTick;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        // Keep screen on
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

        // Load prefs first so _holdMillis is correct
        LoadPrefs();

        // Views
        _hrText = FindViewById<TextView>(Resource.Id.hr_value);
        _exitOverlay = FindViewById(Resource.Id.exitHoldOverlay);
        _countdown = FindViewById<HoldCountdownView>(Resource.Id.holdCountdown);

        // Start service (after permissions)
        if (!BlePermissions.HasAll(this))
            BlePermissions.Request(this);
        else
            StartHrService();

        // Runnable that fires when hold completes
        _triggerExit = new Java.Lang.Runnable(() =>
        {
            if (_holding && (Java.Lang.JavaSystem.CurrentTimeMillis() - _holdStart) >= _holdMillis)
            {
                Haptic(longBuzz: true);
                if (_countdown != null) _countdown.Visibility = ViewStates.Gone;
                ForceCloseApp();
            }
        });

        // Runnable to update the ring every 50ms
        _progressTick = new Java.Lang.Runnable(() =>
        {
            if (!_holding) return;

            long elapsed = Java.Lang.JavaSystem.CurrentTimeMillis() - _holdStart;
            float p = (float)elapsed / (float)_holdMillis;
            if (_countdown != null) _countdown.SetProgress(p);

            if (elapsed < _holdMillis)
            {
                _handler.PostDelayed(_progressTick, 50);
            }
        });

        // Touch handling for the full-screen overlay
        if (_exitOverlay != null)
        {
            _exitOverlay.Touch += (s, e) =>
            {
                switch (e.Event.ActionMasked)
                {
                    case MotionEventActions.Down:
                        if (!IsHoldEnabled())
                        {
                            e.Handled = false;
                            return;
                        }
                        _holdStart = Java.Lang.JavaSystem.CurrentTimeMillis();
                        _holding = true;
                        if (_countdown != null)
                        {
                            _countdown.SetProgress(0f);
                            _countdown.Visibility = ViewStates.Visible;
                        }
                        Haptic();
                        ScheduleCountdownHaptics();
                        _handler.PostDelayed(_triggerExit, _holdMillis);
                        _handler.Post(_progressTick);
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

        // Long-press the HR text to open Settings
        if (_hrText != null)
        {
            _hrText.SetOnLongClickListener(new LongClickListener(() =>
            {
                StartActivity(new Intent(this, typeof(SettingsActivity)));
                return true;
            }));
        }
    }

    private void CancelHold()
    {
        if (!_holding) return;
        _holding = false;
        _handler.RemoveCallbacks(_triggerExit);
        _handler.RemoveCallbacks(_progressTick);
        _handler.RemoveCallbacksAndMessages(null);
        if (_countdown != null) _countdown.Visibility = ViewStates.Gone;
        Toast.MakeText(this, "Hold cancelled", ToastLength.Short).Show();
    }

    private void ScheduleCountdownHaptics()
    {
        // tick once per second until the last second
        int seconds = (int)(_holdMillis / 1000L);
        for (int i = 1; i < seconds; i++)
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
        catch { /* best-effort only */ }
    }

    private void ForceCloseApp()
    {
        try
        {
            // Stop the foreground service cleanly
            StopService(new Intent(this, typeof(HeartRateService)));
            // Close this task so the app is gone from recents
            FinishAndRemoveTask();
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
            StartForegroundService(serviceIntent);
        else
            StartService(serviceIntent);
    }

    // ===== Preferences =====

    private void LoadPrefs()
    {
        var sp = GetSharedPreferences(PREFS, FileCreationMode.Private);

        // hold_enabled (default true) – read but behavior handled at touch-time
        bool enabled = sp.GetBoolean(PREF_HOLD_ENABLED, true);

        // hold_seconds stored as OFFSET 0..10; default offset 5 -> 10 seconds
        int offset = sp.GetInt(PREF_HOLD_SECONDS, 5);
        if (offset < 0) offset = 0;
        if (offset > 10) offset = 10;

        int seconds = 5 + offset; // map 0..10 -> 5..15
        _holdMillis = seconds * 1000L;
    }

    private bool IsHoldEnabled()
    {
        var sp = GetSharedPreferences(PREFS, FileCreationMode.Private);
        return sp.GetBoolean(PREF_HOLD_ENABLED, true);
    }

    // ===== Permissions & Receivers =====

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

        // Reload prefs in case user changed settings
        LoadPrefs();

        if (_updateReceiver == null)
            _updateReceiver = new HrUpdateReceiver(_hrText);

        var filter = new IntentFilter(HeartRateService.ACTION_UPDATE);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            RegisterReceiver(_updateReceiver, filter, ReceiverFlags.NotExported);
        else
            RegisterReceiver(_updateReceiver, filter);
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

        public HrUpdateReceiver(TextView? targetView)
        {
            _targetView = targetView;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == HeartRateService.ACTION_UPDATE)
            {
                int hr = intent.GetIntExtra("hr", 0);
                if (_targetView != null && hr > 0)
                {
                    _targetView.Text = $"{hr} bpm";
                }
            }
        }
    }

    private sealed class LongClickListener : Java.Lang.Object, View.IOnLongClickListener
    {
        private readonly Func<bool> _action;
        public LongClickListener(Func<bool> action) => _action = action;
        public bool OnLongClick(View? v) => _action();
    }
}