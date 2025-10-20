using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware;
using Android.OS;
using Android.Util;
using System;
// Explicit aliases to avoid ambiguity and to log safely
using SysDebug = System.Diagnostics.Debug;
using SysException = System.Exception;

namespace HRPeripheral;

/// <summary>
/// Foreground service that:
/// • Reads HR from SensorManager (TYPE_HEART_RATE)
/// • Auto-pauses/resumes using Samsung off-body (type 34) and/or accelerometer motion
/// • Broadcasts updates to the UI (MainActivity)
/// • Exposes a BLE Heart Rate peripheral (via BlePeripheral)
/// • Shows a persistent notification while running
/// </summary>
[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeHealth | ForegroundService.TypeConnectedDevice
)]
public class HeartRateService : Service, ISensorEventListener
{
    private const string TAG = "HRP/HeartRateService";

    // Foreground notification channel and broadcast action used by MainActivity receiver
    public const string CHANNEL_ID = "hr_channel";
    public static readonly string ACTION_UPDATE = "com.fennell.hrperipheral.UPDATE";
    public const string ACTION_FORCE_CLOSE = "hrperipheral.action.FORCE_CLOSE";

    // Samsung quirk: their LowLatencyOffbodyDetect reports 1 == on-wrist, 0 == off-wrist
    private const bool OFFBODY_ONE_IS_ON_WRIST = true;

    // Feature flag: turn off to disable auto-pause logic entirely
    private const bool AUTO_PAUSE = true;

    // --- Sensors & system handles ---
    private SensorManager? _sm;
    private Sensor? _hrSensor;
    private Sensor? _offBody; // TYPE_LOW_LATENCY_OFFBODY_DETECT (int 34) if present
    private Sensor? _accel;   // accelerometer for motion-based fallback

    // --- BLE peripheral wrapper and state ---
    private BlePeripheral? _ble;
    private bool _advRunning;

    // --- HR and energy tracking ---
    private int _currentHr;
    private DateTime _start;
    private double _kcal;
    private CalorieEstimator? _cal;

    private BroadcastReceiver? _forceCloseReceiver;

    // --- Auto-pause state ---
    private bool _paused;

    // --- Accelerometer motion detection (fallback) ---
    private const int FallbackStillWindowMs = 15_000; // still for 15s -> pause
    private const float MotionEps = 0.8f;             // |mag - g| > eps => motion
    private long _lastMotionMs;

    // --- Watchdog to auto-resume after extended pause (optional) ---
    private Handler? _wdHandler;
    private Java.Lang.IRunnable? _wdTick;
    private long _pausedSinceMs;
    private const int WATCHDOG_PERIOD_MS = 5000;
    private const int WATCHDOG_RESUME_AFTER_MS = 15000; // paused 15s? try resume

    // ===== Logging helpers =====
    private static void LogD(string msg) { try { Log.Debug(TAG, msg); } catch { } SysDebug.WriteLine($"{TAG} D: {msg}"); }
    private static void LogI(string msg) { try { Log.Info(TAG, msg); } catch { } SysDebug.WriteLine($"{TAG} I: {msg}"); }
    private static void LogW(string msg) { try { Log.Warn(TAG, msg); } catch { } SysDebug.WriteLine($"{TAG} W: {msg}"); }
    private static void LogE(string msg, SysException? ex = null)
    {
        try { Log.Error(TAG, ex == null ? msg : $"{msg} :: {ex}"); } catch { }
        SysDebug.WriteLine($"{TAG} E: {msg} :: {ex}");
    }

    /// <summary>
    /// Service bootstrap:
    /// • Create notification channel and post initial foreground notification
    /// • Init sensors, register listeners
    /// • Start BLE advertising
    /// • Register optional broadcast receiver for external force-close
    /// • Start watchdog timer
    /// </summary>
    public override void OnCreate()
    {
        base.OnCreate();

        LogI("OnCreate() starting");
        _start = DateTime.UtcNow;
        _cal = CalorieEstimator.DefaultMale75kgAge35(); // default profile (can be replaced later)
        LogD("CalorieEstimator initialized");

        CreateChannel();
        UpdateNotificationText("Advertising Heart Rate Service");

        _sm = (SensorManager)GetSystemService(SensorService);
        if (_sm == null) { LogE("SensorManager is null!"); }

        // --- Acquire sensors if present ---
        _hrSensor = _sm?.GetDefaultSensor(SensorType.HeartRate);
        LogD($"HR sensor present: {_hrSensor != null}");

        if (AUTO_PAUSE)
        {
            // TYPE_LOW_LATENCY_OFFBODY_DETECT might not exist on all devices (value 34).
            try { _offBody = _sm?.GetDefaultSensor((SensorType)34); } catch { _offBody = null; }
            _accel = _sm?.GetDefaultSensor(SensorType.Accelerometer);
            LogD($"Off-body sensor present: {_offBody != null} (type=34), accel present: {_accel != null}");
        }
        else
        {
            LogD("AUTO_PAUSE disabled");
        }

        // --- Register listeners with appropriate delays ---
        try
        {
            if (_hrSensor != null)
            {
                _sm!.RegisterListener(this, _hrSensor, SensorDelay.Normal);
                LogI("Registered HR sensor @ Normal");
            }

            if (AUTO_PAUSE)
            {
                if (_offBody != null)
                {
                    _sm!.RegisterListener(this, _offBody, SensorDelay.Fastest);
                    LogI("Registered Off-Body sensor (type=34) @ Fastest");
                }

                // Keep accelerometer active as an escape hatch regardless
                if (_accel != null)
                {
                    _sm!.RegisterListener(this, _accel, SensorDelay.Game);
                    LogI("Registered Accelerometer @ Game (fallback + escape hatch)");
                }

                _lastMotionMs = Java.Lang.JavaSystem.CurrentTimeMillis();
            }
        }
        catch (SysException ex)
        {
            LogE("Sensor register error", ex);
        }

        // --- Start BLE peripheral advertising ---
        _ble = new BlePeripheral(this);
        try
        {
            _ble.StartAdvertising();
            _advRunning = true;
            LogI("BLE advertising started");
        }
        catch (SysException ex)
        {
            _advRunning = false;
            LogE("BLE start error", ex);
        }

        // --- Optional broadcast-driven shutdown path (ACTION_FORCE_CLOSE) ---
        _forceCloseReceiver = new ForceCloseReceiver(this);
        var forceFilter = new IntentFilter(ACTION_FORCE_CLOSE);
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                RegisterReceiver(_forceCloseReceiver, forceFilter, ReceiverFlags.NotExported);
                LogI("ForceCloseReceiver registered (Tiramisu+)");
            }
            else
            {
                RegisterReceiver(_forceCloseReceiver, forceFilter);
                LogI("ForceCloseReceiver registered (pre-Tiramisu)");
            }
        }
        catch (SysException ex)
        {
            LogE("Register force-close receiver error", ex);
        }

        // --- Start watchdog loop to auto-resume after prolonged pause ---
        _wdHandler = new Handler(Looper.MainLooper);
        _wdTick = new Java.Lang.Runnable(() =>
        {
            try
            {
                if (_paused && (Java.Lang.JavaSystem.CurrentTimeMillis() - _pausedSinceMs) >= WATCHDOG_RESUME_AFTER_MS)
                {
                    LogW("Watchdog: paused too long — forcing resume attempt");
                    PauseHr(false, "watchdog");
                }
            }
            catch (SysException ex) { LogE("Watchdog tick error", ex); }
            _wdHandler?.PostDelayed(_wdTick, WATCHDOG_PERIOD_MS);
        });
        _wdHandler.PostDelayed(_wdTick, WATCHDOG_PERIOD_MS);

        LogI("OnCreate() complete");
    }

    /// <summary>Unbound service (no binder returned).</summary>
    public override IBinder? OnBind(Intent? intent) => null;

    /// <summary>
    /// Cleanup on service destroy:
    /// • Stop watchdog
    /// • Unregister receivers and sensors
    /// • Stop BLE advertising
    /// • Stop foreground state
    /// </summary>
    public override void OnDestroy()
    {
        LogI("OnDestroy()");

        // Stop watchdog
        try { _wdHandler?.RemoveCallbacks(_wdTick); } catch { }
        _wdTick = null; _wdHandler = null;

        // Receivers
        try
        {
            if (_forceCloseReceiver != null)
            {
                UnregisterReceiver(_forceCloseReceiver);
                LogI("ForceCloseReceiver unregistered");
            }
        }
        catch (SysException ex) { LogW($"UnregisterReceiver error: {ex}"); }

        // Sensors
        try { _sm?.UnregisterListener(this); LogI("All sensors unregistered"); } catch (SysException ex) { LogW($"Unregister all sensors error: {ex}"); }

        // BLE
        try
        {
            if (_advRunning)
            {
                _ble?.StopAdvertising();
                _advRunning = false;
                LogI("BLE advertising stopped");
            }
        }
        catch (SysException ex) { LogW($"BLE stop error: {ex}"); }

        try { StopForeground(true); LogI("Foreground stopped"); } catch (SysException ex) { LogW($"StopForeground error: {ex}"); }

        base.OnDestroy();
    }

    // === ISensorEventListener ===

    /// <summary>Sensor accuracy changes are logged for diagnostics.</summary>
    public void OnAccuracyChanged(Sensor? sensor, SensorStatus accuracy)
    {
        LogD($"OnAccuracyChanged: sensor={(sensor?.Type.ToString() ?? "null")} accuracy={accuracy}");
    }

    /// <summary>
    /// Central sensor event handler:
    /// • Off-body (type 34): toggles paused state immediately
    /// • Accelerometer: motion/no-motion fallback that can pause or resume
    /// • Heart rate: parses BPM, integrates kcal, updates BLE + UI
    /// </summary>
    public void OnSensorChanged(SensorEvent? e)
    {
        if (e?.Sensor == null) return;

        // Verbose event dump (safe-guarded)
        try
        {
            var sType = (int)e.Sensor.Type;
            var name = e.Sensor.Name ?? "(noname)";
            var vendor = e.Sensor.Vendor ?? "(novendor)";
            var values = e.Values;
            string valStr = values == null ? "null" : $"[{string.Join(", ", values)}]";
            LogD($"OnSensorChanged: type={sType} ({e.Sensor.Type}) name={name} vendor={vendor} values={valStr}");
        }
        catch { /* ignore */ }

        if (AUTO_PAUSE)
        {
            // --- Off-body sensor path (fast on/off wrist detection) ---
            // TYPE_LOW_LATENCY_OFFBODY_DETECT (int 34)
            if ((int)e.Sensor.Type == 34)
            {
                float val = (e.Values != null && e.Values.Count > 0) ? e.Values[0] : 0f;

                bool onWrist = OFFBODY_ONE_IS_ON_WRIST ? (val >= 0.5f) : (val < 0.5f);
                LogD($"OffBody event: val={val} => interpreted onWrist={onWrist} (flag={OFFBODY_ONE_IS_ON_WRIST})");

                if (onWrist) PauseHr(false, "on-wrist");
                else PauseHr(true, "off-wrist");
                return;
            }

            // --- Accelerometer fallback path (motion heuristic) ---
            if (e.Sensor.Type == SensorType.Accelerometer)
            {
                if (e.Values == null || e.Values.Count < 3) return;

                float ax = e.Values[0], ay = e.Values[1], az = e.Values[2];
                double mag = Math.Sqrt(ax * ax + ay * ay + az * az);
                long now = Java.Lang.JavaSystem.CurrentTimeMillis();

                LogD($"Accel: ax={ax:F3} ay={ay:F3} az={az:F3} |mag|={mag:F3} gdiff={Math.Abs(mag - 9.80665):F3} paused={_paused}");

                // use 9.80665 m/s^2 as g; significant deviation => motion
                if (Math.Abs(mag - 9.80665) > MotionEps)
                {
                    _lastMotionMs = now;
                    if (_paused) PauseHr(false, "motion-override");
                }
                else
                {
                    long idle = now - _lastMotionMs;
                    if (idle >= FallbackStillWindowMs && !_paused)
                        PauseHr(true, "still");
                }
                return;
            }
        }

        // --- Heart rate path ---
        if (e.Sensor.Type != SensorType.HeartRate) return;

        int hr = (e.Values != null && e.Values.Count > 0)
            ? (int)Math.Round(e.Values[0])
            : 0;

        LogD($"HR event: raw={(e.Values != null && e.Values.Count > 0 ? e.Values[0].ToString("F1") : "null")} parsed={hr} paused={_paused}");

        // Basic validity filter
        if (hr <= 0 || hr > 230) { LogD($"HR ignored (out of range): {hr}"); return; }

        _currentHr = hr;

        // Integrate calories using instantaneous rate (kcal/min) over time-step ~1s
        if (_cal != null)
        {
            double kcalPerMin = _cal.KcalPerMinute(hr);
            _kcal += kcalPerMin / 60.0;
            LogD($"Calorie update: kcal/min={kcalPerMin:F3} total={_kcal:F3}");
        }

        // Notify BLE subscribers (only if advertising is active)
        if (_advRunning)
        {
            _ble?.UpdateHeartRate(hr);
            LogD("BLE GATT characteristic updated with HR");
        }
        else
        {
            LogD("BLE not advertising — HR not sent to subscribers");
        }

        // Notify UI
        BroadcastUpdate();
    }

    // === Pause/Resume helpers ===

    /// <summary>
    /// Centralized pause/resume logic:
    /// • Updates _paused state (idempotent)
    /// • Registers/unregisters HR sensor
    /// • Starts/stops BLE advertising
    /// • Updates foreground notification text
    /// • Broadcasts state to UI
    /// </summary>
    private void PauseHr(bool pause, string reason)
    {
        if (_paused == pause) { LogD($"PauseHr: no change (paused={_paused}) reason={reason}"); return; }

        LogI($"PauseHr: {(pause ? "PAUSE" : "RESUME")} reason={reason}");
        _paused = pause;
        if (pause) _pausedSinceMs = Java.Lang.JavaSystem.CurrentTimeMillis();

        try
        {
            if (pause)
            {
                // Stop reporting HR sensor to reduce work
                if (_hrSensor != null)
                {
                    _sm?.UnregisterListener(this, _hrSensor);
                    LogI("HR sensor unregistered (paused)");
                }

                // Pause BLE advertising to save power
                if (_advRunning)
                {
                    _ble?.StopAdvertising();
                    _advRunning = false;
                    LogI("BLE advertising stopped (paused)");
                }

                UpdateNotificationText($"Paused ({reason})");
            }
            else
            {
                // Resume HR sensor
                if (_hrSensor != null)
                {
                    _sm?.RegisterListener(this, _hrSensor, SensorDelay.Normal);
                    LogI("HR sensor re-registered (resumed)");
                }

                // Resume BLE advertising
                if (!_advRunning)
                {
                    _ble?.StartAdvertising();
                    _advRunning = true;
                    LogI("BLE advertising restarted (resumed)");
                }

                UpdateNotificationText("Advertising Heart Rate Service");
            }
        }
        catch (SysException ex)
        {
            LogE("PauseHr error", ex);
        }

        // Inform UI of new state
        var intent = new Intent(ACTION_UPDATE);
        intent.PutExtra("paused", _paused);
        intent.PutExtra("hr", _currentHr);
        intent.PutExtra("kcal", _kcal);
        intent.SetPackage(PackageName);
        try
        {
            SendBroadcast(intent);
            LogD($"Broadcast (pause change) sent: paused={_paused} hr={_currentHr} kcal={_kcal:F3}");
        }
        catch (SysException ex)
        {
            LogE("Broadcast (pause change) send error", ex);
        }
    }

    /// <summary>
    /// Rebuilds and re-posts the foreground notification with a new one-line status.
    /// </summary>
    private void UpdateNotificationText(string text)
    {
        try
        {
            var notif = new Notification.Builder(this, CHANNEL_ID)
                .SetContentTitle("Heart Rate Broadcasting")
                .SetContentText(text)
                .SetSmallIcon(Android.Resource.Drawable.StatSysDataBluetooth)
                .SetOngoing(true)
                .Build();

            StartForeground(1, notif);
            LogD($"Notification updated: {text}");
        }
        catch (SysException ex)
        {
            LogE("Notification update error", ex);
        }
    }

    /// <summary>
    /// Sends the latest HR/kcal/paused state to the activity via a package-scoped broadcast.
    /// </summary>
    private void BroadcastUpdate()
    {
        var intent = new Intent(ACTION_UPDATE);
        intent.PutExtra("type", 1);
        intent.PutExtra("hr", _currentHr);
        intent.PutExtra("kcal", _kcal);
        intent.PutExtra("paused", _paused);
        intent.SetPackage(PackageName);
        try
        {
            SendBroadcast(intent);
            LogD($"Broadcast (HR) sent: hr={_currentHr} kcal={_kcal:F3} paused={_paused}");
        }
        catch (SysException ex)
        {
            LogE("Broadcast (HR) send error", ex);
        }
    }

    /// <summary>
    /// Creates the O+ notification channel used by this service.
    /// </summary>
    private void CreateChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            try
            {
                var channel = new NotificationChannel(CHANNEL_ID, "Heart Rate", NotificationImportance.Low);
                var mgr = (NotificationManager?)GetSystemService(NotificationService);
                mgr?.CreateNotificationChannel(channel);
                LogD("Notification channel created");
            }
            catch (SysException ex)
            {
                LogE("CreateChannel error", ex);
            }
        }
        else
        {
            LogD("CreateChannel skipped (pre-O)");
        }
    }

    /// <summary>
    /// Local receiver allowing an external broadcast to shut the service down cleanly.
    /// </summary>
    private sealed class ForceCloseReceiver : BroadcastReceiver
    {
        public const string TAG_FC = "HRP/ForceCloseReceiver";
        private readonly HeartRateService _svc;
        public ForceCloseReceiver(HeartRateService svc) => _svc = svc;

        public override void OnReceive(Context? context, Intent? intent)
        {
            try { Log.Info(TAG_FC, $"OnReceive action={intent?.Action ?? "null"}"); } catch { }

            if (intent?.Action != ACTION_FORCE_CLOSE) return;

            // Best-effort cleanup; all wrapped in try-catch to be robust under system pressure.
            try { _svc._sm?.UnregisterListener(_svc); Log.Info(TAG_FC, "Unregistered service from SensorManager"); } catch (SysException) { }
            try
            {
                if (_svc._advRunning)
                {
                    _svc._ble?.StopAdvertising();
                    _svc._advRunning = false;
                    Log.Info(TAG_FC, "Stopped BLE advertising");
                }
            }
            catch (SysException) { }
            try { _svc.StopForeground(true); Log.Info(TAG_FC, "Stopped foreground"); } catch (SysException) { }
            try { _svc.StopSelf(); Log.Info(TAG_FC, "StopSelf called"); } catch (SysException ex) { try { Log.Warn(TAG_FC, $"StopSelf error: {ex}"); } catch { } }
        }
    }
}