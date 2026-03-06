using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Activity = Android.App.Activity;

namespace HRPeripheral.Companion;

[Activity(
    Label = "Session Summary",
    Theme = "@style/AppTheme",
    ScreenOrientation = ScreenOrientation.Portrait
)]
public class SessionSummaryActivity : Activity
{
    // Intent extra keys
    private const string EXTRA_HR_MIN = "hrMin";
    private const string EXTRA_HR_MAX = "hrMax";
    private const string EXTRA_HR_AVG = "hrAvg";
    private const string EXTRA_TOTAL_KCAL = "totalKcal";
    private const string EXTRA_DURATION_SEC = "durationSec";
    private const string EXTRA_SAMPLE_COUNT = "sampleCount";
    private const string EXTRA_ZONE1_SEC = "zone1sec";
    private const string EXTRA_ZONE2_SEC = "zone2sec";
    private const string EXTRA_ZONE3_SEC = "zone3sec";
    private const string EXTRA_ZONE4_SEC = "zone4sec";
    private const string EXTRA_ZONE5_SEC = "zone5sec";
    private const string EXTRA_START_TIME_UTC = "startTimeUtc";

    // Parsed session data
    private int _hrMin;
    private int _hrMax;
    private int _hrAvg;
    private double _totalKcal;
    private long _durationSec;
    private int _sampleCount;
    private readonly long[] _zoneSecs = new long[5];
    private long _startTimeUtcTicks;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_session_summary);

        // Parse intent extras
        _hrMin = Intent?.GetIntExtra(EXTRA_HR_MIN, 0) ?? 0;
        _hrMax = Intent?.GetIntExtra(EXTRA_HR_MAX, 0) ?? 0;
        _hrAvg = Intent?.GetIntExtra(EXTRA_HR_AVG, 0) ?? 0;
        _totalKcal = Intent?.GetDoubleExtra(EXTRA_TOTAL_KCAL, 0.0) ?? 0.0;
        _durationSec = Intent?.GetLongExtra(EXTRA_DURATION_SEC, 0) ?? 0;
        _sampleCount = Intent?.GetIntExtra(EXTRA_SAMPLE_COUNT, 0) ?? 0;
        _zoneSecs[0] = Intent?.GetLongExtra(EXTRA_ZONE1_SEC, 0) ?? 0;
        _zoneSecs[1] = Intent?.GetLongExtra(EXTRA_ZONE2_SEC, 0) ?? 0;
        _zoneSecs[2] = Intent?.GetLongExtra(EXTRA_ZONE3_SEC, 0) ?? 0;
        _zoneSecs[3] = Intent?.GetLongExtra(EXTRA_ZONE4_SEC, 0) ?? 0;
        _zoneSecs[4] = Intent?.GetLongExtra(EXTRA_ZONE5_SEC, 0) ?? 0;
        _startTimeUtcTicks = Intent?.GetLongExtra(EXTRA_START_TIME_UTC, 0) ?? 0;

        // Populate UI
        PopulateStats();
        BuildZoneBreakdown();

        // Button handlers
        var btnExport = FindViewById<Button>(Resource.Id.btnExportCsv);
        var btnShare = FindViewById<Button>(Resource.Id.btnShare);
        var btnDone = FindViewById<Button>(Resource.Id.btnDone);

        if (btnExport != null) btnExport.Click += (s, e) => ExportCsv();
        if (btnShare != null) btnShare.Click += (s, e) => ShareSummary();
        if (btnDone != null) btnDone.Click += (s, e) => Finish();
    }

    private void PopulateStats()
    {
        var elapsed = TimeSpan.FromSeconds(_durationSec);

        var txtDuration = FindViewById<TextView>(Resource.Id.txtSummaryDuration);
        var txtAvgHr = FindViewById<TextView>(Resource.Id.txtSummaryAvgHr);
        var txtMinHr = FindViewById<TextView>(Resource.Id.txtSummaryMinHr);
        var txtMaxHr = FindViewById<TextView>(Resource.Id.txtSummaryMaxHr);
        var txtCalories = FindViewById<TextView>(Resource.Id.txtSummaryCalories);

        if (txtDuration != null) txtDuration.Text = SessionTracker.FormatDuration(elapsed);
        if (txtAvgHr != null) txtAvgHr.Text = _hrAvg > 0 ? _hrAvg.ToString() : "--";
        if (txtMinHr != null) txtMinHr.Text = _hrMin > 0 ? _hrMin.ToString() : "--";
        if (txtMaxHr != null) txtMaxHr.Text = _hrMax > 0 ? _hrMax.ToString() : "--";
        if (txtCalories != null) txtCalories.Text = $"{_totalKcal:F1}";
    }

    private void BuildZoneBreakdown()
    {
        var container = FindViewById<LinearLayout>(Resource.Id.zoneSummaryBreakdown);
        if (container == null) return;
        container.RemoveAllViews();

        long maxSec = 1;
        for (int i = 0; i < 5; i++)
        {
            if (_zoneSecs[i] > maxSec)
                maxSec = _zoneSecs[i];
        }

        for (int i = 0; i < 5; i++)
        {
            var row = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal
            };
            row.SetPadding(0, 6, 0, 6);
            row.SetGravity(Android.Views.GravityFlags.CenterVertical);

            var (r, g, b) = HeartRateZone.GetZoneColor(i + 1);
            var zoneName = HeartRateZone.Zones[i].Name;

            // Zone label (e.g. "Z1 Very Light")
            var label = new TextView(this)
            {
                Text = $"Z{i + 1} {zoneName}",
                TextSize = 13f,
            };
            label.SetTextColor(Color.Rgb(r, g, b));
            label.SetMinWidth(200);
            label.LayoutParameters = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.WrapContent,
                LinearLayout.LayoutParams.WrapContent);

            // Color bar
            var bar = new View(this);
            bar.SetBackgroundColor(Color.Rgb(r, g, b));
            float weight = (float)_zoneSecs[i] / maxSec;
            if (weight < 0.01f && _zoneSecs[i] > 0) weight = 0.01f;
            var barParams = new LinearLayout.LayoutParams(0, 24) { Weight = weight };
            barParams.SetMargins(8, 0, 8, 0);
            bar.LayoutParameters = barParams;

            // Time label
            var timeSpan = TimeSpan.FromSeconds(_zoneSecs[i]);
            string timeStr = timeSpan.TotalSeconds < 60
                ? $"{timeSpan.TotalSeconds:F0}s"
                : $"{(int)timeSpan.TotalMinutes}:{timeSpan.Seconds:D2}";

            var timeLabel = new TextView(this)
            {
                Text = timeStr,
                TextSize = 13f,
            };
            timeLabel.SetTextColor(Color.Rgb(0xCC, 0xCC, 0xCC));
            timeLabel.LayoutParameters = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.WrapContent,
                LinearLayout.LayoutParams.WrapContent);

            row.AddView(label);
            row.AddView(bar);
            row.AddView(timeLabel);
            container.AddView(row);
        }
    }

    private void ExportCsv()
    {
        try
        {
            var startTime = new DateTime(_startTimeUtcTicks, DateTimeKind.Utc);
            var fileName = $"hr_session_{startTime:yyyyMMdd_HHmmss}.csv";

            var downloadsDir = Android.OS.Environment.GetExternalStoragePublicDirectory(
                Android.OS.Environment.DirectoryDownloads);
            if (downloadsDir == null)
            {
                Toast.MakeText(this, "Downloads folder not available.", ToastLength.Short)?.Show();
                return;
            }

            if (!downloadsDir.Exists())
                downloadsDir.Mkdirs();

            var file = new Java.IO.File(downloadsDir, fileName);
            using var writer = new Java.IO.FileWriter(file);

            writer.Write("Metric,Value\n");
            writer.Write($"Start (UTC),{startTime:yyyy-MM-dd HH:mm:ss}\n");
            writer.Write($"Duration (s),{_durationSec}\n");
            writer.Write($"Samples,{_sampleCount}\n");
            writer.Write($"HR Avg,{_hrAvg}\n");
            writer.Write($"HR Min,{_hrMin}\n");
            writer.Write($"HR Max,{_hrMax}\n");
            writer.Write($"Calories,{_totalKcal:F1}\n");

            for (int i = 0; i < 5; i++)
            {
                var zoneName = HeartRateZone.Zones[i].Name;
                writer.Write($"Zone {i + 1} ({zoneName}) (s),{_zoneSecs[i]}\n");
            }

            writer.Flush();

            Toast.MakeText(this, $"Saved to Downloads/{fileName}", ToastLength.Long)?.Show();
        }
        catch (Exception ex)
        {
            Toast.MakeText(this, $"Export failed: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    private void ShareSummary()
    {
        var elapsed = TimeSpan.FromSeconds(_durationSec);
        var startTime = new DateTime(_startTimeUtcTicks, DateTimeKind.Utc).ToLocalTime();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Workout Summary");
        sb.AppendLine($"Date: {startTime:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Duration: {SessionTracker.FormatDuration(elapsed)}");
        sb.AppendLine();
        sb.AppendLine($"Avg HR: {_hrAvg} bpm");
        sb.AppendLine($"Min HR: {_hrMin} bpm");
        sb.AppendLine($"Max HR: {_hrMax} bpm");
        sb.AppendLine($"Calories: {_totalKcal:F1} kcal");
        sb.AppendLine();
        sb.AppendLine("Zone Time:");
        for (int i = 0; i < 5; i++)
        {
            var zoneName = HeartRateZone.Zones[i].Name;
            var ts = TimeSpan.FromSeconds(_zoneSecs[i]);
            string timeStr = ts.TotalSeconds < 60
                ? $"{ts.TotalSeconds:F0}s"
                : $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            sb.AppendLine($"  Z{i + 1} {zoneName}: {timeStr}");
        }

        var shareIntent = new Intent(Intent.ActionSend);
        shareIntent.SetType("text/plain");
        shareIntent.PutExtra(Intent.ExtraSubject, "Workout Summary");
        shareIntent.PutExtra(Intent.ExtraText, sb.ToString());

        StartActivity(Intent.CreateChooser(shareIntent, "Share Summary"));
    }

    /// <summary>
    /// Launches the SessionSummaryActivity with data from the given SessionTracker.
    /// </summary>
    public static void LaunchSummary(Activity context, SessionTracker session)
    {
        var now = DateTime.UtcNow;
        var elapsed = session.Elapsed(now);
        var zoneTimes = session.GetZoneTimes();

        var intent = new Intent(context, typeof(SessionSummaryActivity));
        intent.PutExtra(EXTRA_HR_MIN, session.HrMin);
        intent.PutExtra(EXTRA_HR_MAX, session.HrMax);
        intent.PutExtra(EXTRA_HR_AVG, session.HrAvg);
        intent.PutExtra(EXTRA_TOTAL_KCAL, session.TotalKcal);
        intent.PutExtra(EXTRA_DURATION_SEC, (long)elapsed.TotalSeconds);
        intent.PutExtra(EXTRA_SAMPLE_COUNT, session.SampleCount);
        intent.PutExtra(EXTRA_ZONE1_SEC, (long)zoneTimes[0].TotalSeconds);
        intent.PutExtra(EXTRA_ZONE2_SEC, (long)zoneTimes[1].TotalSeconds);
        intent.PutExtra(EXTRA_ZONE3_SEC, (long)zoneTimes[2].TotalSeconds);
        intent.PutExtra(EXTRA_ZONE4_SEC, (long)zoneTimes[3].TotalSeconds);
        intent.PutExtra(EXTRA_ZONE5_SEC, (long)zoneTimes[4].TotalSeconds);
        intent.PutExtra(EXTRA_START_TIME_UTC, session.StartTime.Ticks);

        context.StartActivity(intent);
    }
}
