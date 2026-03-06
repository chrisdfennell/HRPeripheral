using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Widget;

namespace HRPeripheral.Companion;

[Activity(
    Label = "Settings",
    Theme = "@style/AppTheme",
    ScreenOrientation = ScreenOrientation.Portrait
)]
public class CompanionSettingsActivity : Activity
{
    private Switch? _switchCalMale;
    private SeekBar? _seekWeight;
    private SeekBar? _seekAge;
    private TextView? _txtWeightValue;
    private TextView? _txtAgeValue;
    private TextView? _txtMaxHr;
    private TextView? _txtVersion;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_companion_settings);

        _switchCalMale = FindViewById<Switch>(Resource.Id.switchCalMale);
        _seekWeight = FindViewById<SeekBar>(Resource.Id.seekWeight);
        _seekAge = FindViewById<SeekBar>(Resource.Id.seekAge);
        _txtWeightValue = FindViewById<TextView>(Resource.Id.txtWeightValue);
        _txtAgeValue = FindViewById<TextView>(Resource.Id.txtAgeValue);
        _txtMaxHr = FindViewById<TextView>(Resource.Id.txtMaxHr);
        _txtVersion = FindViewById<TextView>(Resource.Id.txtVersion);

        // Sex toggle
        if (_switchCalMale != null)
        {
            _switchCalMale.CheckedChange += (s, e) =>
            {
                SavePref(sp => sp.PutBoolean(HrpPrefs.KEY_CAL_MALE, e.IsChecked));
            };
        }

        // Weight slider
        if (_seekWeight != null)
        {
            _seekWeight.Max = HrpPrefs.MAX_CAL_WEIGHT_KG - HrpPrefs.MIN_CAL_WEIGHT_KG;
            _seekWeight.ProgressChanged += (s, e) =>
            {
                if (!e.FromUser) return;
                int kg = e.Progress + HrpPrefs.MIN_CAL_WEIGHT_KG;
                if (_txtWeightValue != null) _txtWeightValue.Text = $"{kg} kg";
                SavePref(sp => sp.PutInt(HrpPrefs.KEY_CAL_WEIGHT_KG, kg));
            };
        }

        // Age slider
        if (_seekAge != null)
        {
            _seekAge.Max = HrpPrefs.MAX_CAL_AGE - HrpPrefs.MIN_CAL_AGE;
            _seekAge.ProgressChanged += (s, e) =>
            {
                if (!e.FromUser) return;
                int age = e.Progress + HrpPrefs.MIN_CAL_AGE;
                if (_txtAgeValue != null) _txtAgeValue.Text = $"{age}";
                UpdateMaxHrLabel(age);
                SavePref(sp => sp.PutInt(HrpPrefs.KEY_CAL_AGE, age));
            };
        }

        // Version label
        if (_txtVersion != null)
        {
            try
            {
                var info = PackageManager?.GetPackageInfo(PackageName!, 0);
                _txtVersion.Text = $"Version {info?.VersionName ?? "?"}";
            }
            catch { _txtVersion.Text = "Version ?"; }
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        var sp = GetSharedPreferences(HrpPrefs.PREFS_NAME, FileCreationMode.Private)!;
        bool male = sp.GetBoolean(HrpPrefs.KEY_CAL_MALE, HrpPrefs.DEFAULT_CAL_MALE);
        int weight = HrpPrefs.ClampWeight(sp.GetInt(HrpPrefs.KEY_CAL_WEIGHT_KG, HrpPrefs.DEFAULT_CAL_WEIGHT_KG));
        int age = HrpPrefs.ClampAge(sp.GetInt(HrpPrefs.KEY_CAL_AGE, HrpPrefs.DEFAULT_CAL_AGE));

        if (_switchCalMale != null) _switchCalMale.Checked = male;
        if (_seekWeight != null)
        {
            _seekWeight.Progress = weight - HrpPrefs.MIN_CAL_WEIGHT_KG;
            if (_txtWeightValue != null) _txtWeightValue.Text = $"{weight} kg";
        }
        if (_seekAge != null)
        {
            _seekAge.Progress = age - HrpPrefs.MIN_CAL_AGE;
            if (_txtAgeValue != null) _txtAgeValue.Text = $"{age}";
        }
        UpdateMaxHrLabel(age);
    }

    private void UpdateMaxHrLabel(int age)
    {
        if (_txtMaxHr != null)
            _txtMaxHr.Text = $"Max HR: {HeartRateZone.MaxHr(age)} bpm";
    }

    private void SavePref(Action<ISharedPreferencesEditor> action)
    {
        var sp = GetSharedPreferences(HrpPrefs.PREFS_NAME, FileCreationMode.Private)!;
        using var edit = sp.Edit()!;
        action(edit);
        edit.Apply();
    }
}
