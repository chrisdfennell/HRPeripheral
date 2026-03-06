using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Path = Android.Graphics.Path;

namespace HRPeripheral.Companion;

/// <summary>
/// Simplified HR graph for the phone companion app.
/// Draws a real-time HR waveform with zone-colored line.
/// </summary>
[Register("hrperipheral.companion.CompanionHrGraphView")]
public class CompanionHrGraphView : View
{
    readonly Queue<int> _points = new();
    int _count;
    const int MaxPoints = 180; // ~3 minutes at ~1 Hz

    int _yMin = 50;
    int _yMax = 200;
    int _lastHr;
    int _age = HrpPrefs.DEFAULT_CAL_AGE;

    readonly Paint _line = new() { StrokeWidth = 3, Color = Color.White, AntiAlias = true };
    readonly Paint _fill = new() { AntiAlias = true, Color = Color.Argb(40, 255, 255, 255) };
    readonly Paint _grid = new() { StrokeWidth = 1, Color = Color.Argb(255, 50, 50, 50), AntiAlias = true };
    readonly Paint _label = new() { Color = Color.Argb(200, 180, 180, 180), TextSize = 24f, AntiAlias = true };
    readonly Paint _marker = new() { Color = Color.White, AntiAlias = true };
    readonly Path _path = new();
    readonly Path _pathFill = new();
    readonly Rect _tb = new();

    public CompanionHrGraphView(Context context, IAttributeSet? attrs) : base(context, attrs) => Init();
    public CompanionHrGraphView(Context context, IAttributeSet? attrs, int defStyleAttr) : base(context, attrs, defStyleAttr) => Init();
    public CompanionHrGraphView(Context context) : base(context) => Init();
    protected CompanionHrGraphView(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer) => Init();

    private void Init()
    {
        SetBackgroundColor(Color.Argb(255, 20, 20, 20));
        _line.SetStyle(Paint.Style.Stroke);
        _line.StrokeCap = Paint.Cap.Round;
        _line.StrokeJoin = Paint.Join.Round;
        _fill.SetStyle(Paint.Style.Fill);
    }

    public void Push(int hr)
    {
        _lastHr = hr;
        if (_count >= MaxPoints) _points.Dequeue(); else _count++;
        _points.Enqueue(hr);
        Invalidate();
    }

    public void SetAge(int age) { _age = HrpPrefs.ClampAge(age); Invalidate(); }
    public void Clear() { _points.Clear(); _count = 0; _lastHr = 0; Invalidate(); }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        int w = Width, h = Height;
        if (w <= 0 || h <= 0) return;

        canvas.DrawColor(Color.Argb(255, 20, 20, 20));

        float padL = 40f, padR = 16f, padT = 12f, padB = 24f;
        float chartW = w - padL - padR;
        float chartH = h - padT - padB;
        if (chartW <= 4 || chartH <= 4) return;

        float left = padL, top = padT, right = padL + chartW, bottom = padT + chartH;

        // Grid lines (every 50 bpm)
        for (int y = _yMin; y <= _yMax; y += 50)
        {
            float yy = MapY(y, top, chartH);
            canvas.DrawLine(left, yy, right, yy, _grid);
            string lab = y.ToString();
            _label.GetTextBounds(lab, 0, lab.Length, _tb);
            canvas.DrawText(lab, left - _tb.Width() - 6, yy + _tb.Height() * 0.35f, _label);
        }

        if (_count < 2) return;

        // Zone-colored line
        int zone = HeartRateZone.GetZone(_lastHr, _age);
        var (r, g, b) = HeartRateZone.GetZoneColor(zone > 0 ? zone : 1);
        _line.Color = Color.Rgb(r, g, b);
        _fill.Color = Color.Argb(40, r, g, b);

        // Build path
        _path.Reset();
        _pathFill.Reset();
        float stepX = chartW / Math.Max(1, MaxPoints - 1);
        var tmp = _points.ToArray();
        int n = tmp.Length;

        for (int k = 0; k < n; k++)
        {
            float x = left + stepX * k;
            float y = MapY(tmp[k], top, chartH);
            if (k == 0) _path.MoveTo(x, y);
            else _path.LineTo(x, y);
        }

        _pathFill.AddPath(_path);
        _pathFill.LineTo(left + stepX * (n - 1), bottom);
        _pathFill.LineTo(left, bottom);
        _pathFill.Close();

        canvas.DrawPath(_pathFill, _fill);
        canvas.DrawPath(_path, _line);

        // Live marker
        float lastX = left + stepX * (n - 1);
        float lastY = MapY(_lastHr, top, chartH);
        canvas.DrawCircle(lastX, lastY, 6f, _marker);
    }

    private float MapY(int hr, float top, float chartH)
    {
        int clamped = Math.Max(_yMin, Math.Min(_yMax, hr));
        float t = (clamped - _yMin) / (float)(_yMax - _yMin);
        return top + (1f - t) * chartH;
    }
}
