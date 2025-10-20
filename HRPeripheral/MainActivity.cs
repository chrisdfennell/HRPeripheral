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

using HRPeripheral.Views; // HoldCountdownView, HrGraphView

namespace HRPeripheral;

[Activity(
    Label = "HR Peripheral",
    MainLauncher = true,
    Theme = "@android:style/Theme.DeviceDefault",
    Exported = true, // required on API 31+ when you have an intent filter
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize
)]
public class MainActivity : Activity
{
    // SharedPreferences
    private const string PREFS = "hrp_prefs";
    private const string PREF_HOLD_ENABLED = "hold_enabled";
    private const string PREF_HOLD_SECONDS = "hold_seconds"; // stored as offset 0..10 (maps to 5..15s)

    // Graph (full-screen minus top button)
    private HrGraphView? _hrGraph;

    private ImageButton? _btnSettings;
    private HrUpdateReceiver? _updateReceiver;

    // Press & hold to exit (with visible HoldCountdownView)
    private View? _exitOverlay;
    private HoldCountdownView? _countdown;
    private long _holdStart;
    private bool _holding;
    private long _holdMillis = 10_000L; // overwritten by prefs
    private readonly Handler _handler = new Handler(Looper.MainLooper);
    private Java.Lang.IRunnable? _triggerExit;
    private Java.Lang.IRunnable? _progressTick;
    private Java.Lang.IRunnable? _startCountdown; // starts after pre-hold delay

    // Pre-hold delay so taps don’t trigger the countdown (1s default)
    private const int PRE_HOLD_DELAY_MS = 1000;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        // Keep screen on
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

        // Load prefs first so _holdMillis is correct
        LoadPrefs();

        // Views
        _hrGraph = FindViewById<HrGraphView>(Resource.Id.hr_graph);
        _exitOverlay = FindViewById(Resource.Id.exitHoldOverlay);
        _countdown = FindViewById<HoldCountdownView>(Resource.Id.holdCountdown);
        _btnSettings = FindViewById<ImageButton>(Resource.Id.btnSettings);

        // Ensure the gear is ABOVE the overlay (z-order + elevation)
        _btnSettings?.Post(() =>
        {
            _btnSettings.BringToFront();
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
                _btnSettings.Elevation = 8f;
        });

        // Settings button click
        _btnSettings?.SetOnClickListener(new ClickListener(() =>
        {
            StartActivity(new Intent(this, typeof(SettingsActivity)));
        }));

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

        // Deferred start of countdown (after PRE_HOLD_DELAY_MS)
        _startCountdown = new Java.Lang.Runnable(() =>
        {
            if (!_holding) return; // user let go early

            // Visuals + feedback now that the long-press is confirmed
            _holdStart = Java.Lang.JavaSystem.CurrentTimeMillis();
            if (_countdown != null)
            {
                _countdown.SetProgress(0f);
                _countdown.Visibility = ViewStates.Visible;
            }
            // Fade settings while holding (optional polish)
            _btnSettings?.Animate()?.Alpha(0f)?.SetDuration(150)?.Start();

            Haptic();
            ScheduleCountdownHaptics();
            _handler.PostDelayed(_triggerExit, _holdMillis);
            _handler.Post(_progressTick);
        });

        // Touch handling for the full-screen overlay
        if (_exitOverlay != null)
        {
            _exitOverlay.Touch += (s, e) =>
            {
                // If touch begins on the gear, let the gear handle it
                if (e.Event.ActionMasked == MotionEventActions.Down && _btnSettings != null)
                {
                    if (IsTouchInsideView(_btnSettings, e.Event))
                    {
                        e.Handled = false; // pass to button
                        return;
                    }
                }

                switch (e.Event.ActionMasked)
                {
                    case MotionEventActions.Down:
                        if (!IsHoldEnabled())
                        {
                            e.Handled = false;
                            return;
                        }
                        _holding = true;
                        // Require PRE_HOLD_DELAY_MS of continuous hold before showing countdown
                        _handler.PostDelayed(_startCountdown, PRE_HOLD_DELAY_MS);
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

    // Optional overflow menu path to Settings
    public override bool OnCreateOptionsMenu(IMenu menu)
    {
        menu.Add(0, 1001, 0, "Settings");
        return true;
    }
    public override bool OnOptionsItemSelected(IMenuItem item)
    {
        if (item.ItemId == 1001)
        {
            StartActivity(new Intent(this, typeof(SettingsActivity)));
            return true;
        }
        return base.OnOptionsItemSelected(item);
    }

    private void CancelHold()
    {
        // Always clear any pending delayed start
        _handler.RemoveCallbacks(_startCountdown);

        if (!_holding) return;

        _holding = false;

        // Cancel anything scheduled
        _handler.RemoveCallbacks(_triggerExit);
        _handler.RemoveCallbacks(_progressTick);

        if (_countdown != null) _countdown.Visibility = ViewStates.Gone;
        _btnSettings?.Animate()?.Alpha(1f)?.SetDuration(150)?.Start();

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
        bool _ = sp.GetBoolean(PREF_HOLD_ENABLED, true);

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
            _updateReceiver = new HrUpdateReceiver(this);

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
    /// Receives heart-rate updates from HeartRateService and updates the UI + graph.
    /// Also dims UI when service reports paused=true.
    /// </summary>
    private class HrUpdateReceiver : BroadcastReceiver
    {
        private readonly MainActivity _host;
        public HrUpdateReceiver(MainActivity host) => _host = host;

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != HeartRateService.ACTION_UPDATE) return;

            int hr = intent.GetIntExtra("hr", 0);
            bool paused = intent.GetBooleanExtra("paused", false);

            // Graph – use Push for your view
            if (hr > 0)
                _host._hrGraph?.Push(hr);

            // Dim/undim UI based on pause state
            float alpha = paused ? 0.5f : 1f;
            _host._hrGraph?.Animate()?.Alpha(alpha)?.SetDuration(150)?.Start();
        }
    }

    private sealed class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        public ClickListener(Action action) => _action = action;
        public void OnClick(View? v) => _action();
    }

    // ===== Helpers =====

    /// <summary>
    /// Returns true if the MotionEvent DOWN occurred inside the given view.
    /// Uses raw screen coordinates to handle overlays correctly.
    /// </summary>
    private static bool IsTouchInsideView(View v, MotionEvent e)
    {
        if (v.Visibility != ViewStates.Visible) return false;
        int[] loc = new int[2];
        v.GetLocationOnScreen(loc);
        var left = loc[0];
        var top = loc[1];
        var right = left + v.Width;
        var bottom = top + v.Height;
        float x = e.RawX, y = e.RawY;
        return x >= left && x <= right && y >= top && y <= bottom;
    }
}
