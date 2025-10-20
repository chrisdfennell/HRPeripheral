using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Util;
using Android.Views;
using System;
using System.Collections.Generic;
// Disambiguate Path:
using APath = Android.Graphics.Path;

namespace HRPeripheral.Views;

// This Register lets you use <hrperipheral.views.HrGraphView .../> in AXML.
// If you prefer <HRPeripheral.Views.HrGraphView .../>, you can keep both working.
[Register("hrperipheral.views.HrGraphView")]
public class HrGraphView : View
{
    // ===== Data =====
    readonly Queue<int> _points = new();
    int _count = 0;                      // faster than _points.Count in tight loops
    const int DefaultMaxPoints = 120;    // ~2 minutes at ~1 Hz
    int _maxPoints = DefaultMaxPoints;

    // Y range (bpm)
    int _yMin = 50;
    int _yMax = 190;

    // Optional smoothing (simple moving average)
    public bool UseSmoothing { get; set; } = true;
    public int SmoothingWindow { get; set; } = 3; // 1..7 reasonable on watch

    // State
    bool _paused = false;
    int _lastHr = 0;

    // ===== Paints / drawing objects =====
    readonly Paint _axis = new()
    {
        StrokeWidth = 2,
        Color = Color.Argb(255, 70, 70, 70),
        AntiAlias = true
    };

    readonly Paint _gridMajor = new()
    {
        StrokeWidth = 1.5f,
        Color = Color.Argb(255, 60, 60, 60),
        AntiAlias = true
    };

    readonly Paint _gridMinor = new()
    {
        StrokeWidth = 1,
        Color = Color.Argb(255, 45, 45, 45),
        AntiAlias = true
    };

    readonly Paint _line = new()
    {
        StrokeWidth = 4,
        Color = Color.White,
        AntiAlias = true
    };

    readonly Paint _lineFill = new()
    {
        AntiAlias = true,
        Color = Color.Argb(64, 255, 255, 255)
    };

    readonly Paint _label = new()
    {
        Color = Color.Argb(255, 210, 210, 210),
        TextSize = 20f,
        AntiAlias = true
    };

    readonly Paint _badge = new()
    {
        Color = Color.Argb(220, 255, 255, 255),
        TextSize = 26f,
        FakeBoldText = true,
        AntiAlias = true
    };

    readonly Paint _marker = new()
    {
        Color = Color.Argb(255, 255, 255, 255),
        AntiAlias = true
    };

    readonly Paint _pausedTint = new()
    {
        Color = Color.Argb(90, 0, 0, 0),
        AntiAlias = true
    };

    readonly Paint _pausedText = new()
    {
        Color = Color.Argb(230, 255, 200, 0),
        TextSize = 28f,
        FakeBoldText = true,
        AntiAlias = true
    };

    // Reusable objects
    readonly APath _path = new();
    readonly APath _pathFill = new();
    readonly Rect _textBounds = new();
    LinearGradient? _bgGradient;

    // ===== Constructors required for XML inflation =====

    // Used when inflating from XML
    public HrGraphView(Context context, IAttributeSet? attrs) : base(context, attrs)
    {
        Init();
    }

    // Used when inflating from XML with a style
    public HrGraphView(Context context, IAttributeSet? attrs, int defStyleAttr) : base(context, attrs, defStyleAttr)
    {
        Init();
    }

    // Used when creating from code
    public HrGraphView(Context context) : base(context)
    {
        Init();
    }

    // Used by the runtime for JNI ownership marshaling
    protected HrGraphView(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
        Init();
    }

    private void Init()
    {
        try
        {
            SetBackgroundColor(Color.Black);

            _line.SetStyle(Paint.Style.Stroke);
            _line.StrokeCap = Paint.Cap.Round;
            _line.StrokeJoin = Paint.Join.Round;

            _lineFill.SetStyle(Paint.Style.Fill);

            // dashed effect for minor grid
            _gridMinor.SetPathEffect(new DashPathEffect(new float[] { 6, 6 }, 0));

            // improve rendering
            SetLayerType(LayerType.Hardware, null);
        }
        catch
        {
            // keep inflater resilient
        }
    }

    // ===== Public API =====

    public void Push(int hr)
    {
        _lastHr = hr;
        if (_count >= _maxPoints) _points.Dequeue(); else _count++;
        _points.Enqueue(hr);
        Invalidate();
    }

    public void Clear()
    {
        _points.Clear();
        _count = 0;
        _lastHr = 0;
        Invalidate();
    }

    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        Invalidate();
    }

    /// <summary>Change the HR bounds (e.g., 40..200). Clamps min&lt;max and redraws.</summary>
    public void SetRange(int minBpm, int maxBpm)
    {
        if (maxBpm <= minBpm) return;
        _yMin = minBpm;
        _yMax = maxBpm;
        Invalidate();
    }

    /// <summary>Change the horizontal time span (number of samples kept).</summary>
    public void SetMaxPoints(int maxPoints)
    {
        if (maxPoints < 10) maxPoints = 10;
        _maxPoints = maxPoints;

        // Trim if needed
        while (_count > _maxPoints)
        {
            _points.Dequeue();
            _count--;
        }
        Invalidate();
    }

    // ===== View lifecycle =====

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        if (w <= 0 || h <= 0) return;

        // Force simple sRGB ARGB ints via the int[] overload to avoid ColorSpace mismatch.
        int cTop = Color.Argb(255, 18, 18, 18).ToArgb();
        int cBottom = Color.Argb(255, 8, 8, 8).ToArgb();

        _bgGradient = new LinearGradient(
            0, 0, 0, h,
            new int[] { cTop, cBottom },   // <-- int[] overload is safe
            (float[])null,
            Shader.TileMode.Clamp
        );
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        int w = Width, h = Height;
        if (w <= 0 || h <= 0) return;

        // background
        if (_bgGradient != null)
        {
            using var bg = new Paint() { AntiAlias = true };
            bg.SetShader(_bgGradient);
            canvas.DrawRect(0, 0, w, h, bg);
        }
        else
        {
            canvas.DrawColor(Color.Black);
        }

        // chart paddings
        float padL = 36f;   // room for Y labels
        float padR = 12f;
        float padT = 10f;
        float padB = 22f;   // room for X labels

        float chartW = w - padL - padR;
        float chartH = h - padT - padB;
        if (chartW <= 4 || chartH <= 4) return;

        float left = padL, top = padT, right = padL + chartW, bottom = padT + chartH;

        // axes
        canvas.DrawLine(left, bottom, right, bottom, _axis); // X
        canvas.DrawLine(left, top, left, bottom, _axis);     // Y

        // grid lines and labels
        DrawGridAndLabels(canvas, left, top, right, bottom, chartW, chartH);

        // curve
        if (_count >= 2)
        {
            DrawCurve(canvas, left, top, chartW, chartH);

            // live marker (latest point)
            float stepX = chartW / Math.Max(1, _maxPoints - 1);
            int lastIndex = Math.Max(0, _count - 1);
            float lastX = left + stepX * lastIndex;
            float lastY = MapY(_lastHr, top, chartH);
            canvas.DrawCircle(lastX, lastY, 5f, _marker);

            // current HR badge (top-right)
            if (_lastHr > 0)
            {
                string txt = $"{_lastHr} bpm";
                _badge.GetTextBounds(txt, 0, txt.Length, _textBounds);
                float bx = right - _textBounds.Width() - 6;
                float by = top + _textBounds.Height() + 2;
                canvas.DrawText(txt, bx, by, _badge);
            }
        }
        else
        {
            // empty hint
            const string hint = "waiting for HR…";
            _label.GetTextBounds(hint, 0, hint.Length, _textBounds);
            canvas.DrawText(hint, left + 8, top + _textBounds.Height() + 4, _label);
        }

        // paused overlay
        if (_paused)
        {
            canvas.DrawRect(left, top, right, bottom, _pausedTint);
            const string paused = "PAUSED";
            _pausedText.GetTextBounds(paused, 0, paused.Length, _textBounds);
            float cx = (left + right) * 0.5f - _textBounds.Width() * 0.5f;
            float cy = (top + bottom) * 0.5f + _textBounds.Height() * 0.5f;
            canvas.DrawText(paused, cx, cy, _pausedText);
        }
    }

    // Provide sensible default size for wrap_content (optional; your AXML sets 56dp height)
    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        int desiredW = (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, 200, Resources?.DisplayMetrics);
        int desiredH = (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, 56, Resources?.DisplayMetrics);

        int width = ResolveSize(desiredW, widthMeasureSpec);
        int height = ResolveSize(desiredH, heightMeasureSpec);
        SetMeasuredDimension(width, height);
    }

    // ===== Drawing helpers =====

    void DrawGridAndLabels(Canvas canvas, float left, float top, float right, float bottom, float chartW, float chartH)
    {
        // Y ticks: major every 50 bpm, minor every 25 bpm within [yMin, yMax]
        int yMajor = 50;
        int yMinor = 25;

        for (int y = RoundUp(_yMin, yMinor); y <= _yMax; y += yMinor)
        {
            float yy = MapY(y, top, chartH);
            var isMajor = y % yMajor == 0;

            canvas.DrawLine(left, yy, right, yy, isMajor ? _gridMajor : _gridMinor);

            if (isMajor)
            {
                string lab = y.ToString();
                _label.GetTextBounds(lab, 0, lab.Length, _textBounds);
                canvas.DrawText(lab, left - 8 - _textBounds.Width(), yy + _textBounds.Height() * 0.35f, _label);
            }
        }

        // X ticks: show 30s, 60s, 90s markers assuming ~1Hz sampling
        int secondsSpan = _maxPoints; // approx
        int[] xMajors = { 30, 60, 90 };
        float stepX = chartW / Math.Max(1, _maxPoints - 1);

        foreach (var sec in xMajors)
        {
            if (sec >= secondsSpan) continue;
            float xx = left + sec * stepX;
            canvas.DrawLine(xx, top, xx, bottom, _gridMajor);

            string lab = $"{sec}s";
            _label.GetTextBounds(lab, 0, lab.Length, _textBounds);
            float lx = xx - _textBounds.Width() * 0.5f;
            float ly = bottom + _textBounds.Height() + 2;
            canvas.DrawText(lab, lx, ly, _label);
        }
    }

    void DrawCurve(Canvas canvas, float left, float top, float chartW, float chartH)
    {
        _path.Reset();
        _pathFill.Reset();

        int n = _count;
        if (n <= 1) return;

        int win = Math.Max(1, Math.Min(UseSmoothing ? SmoothingWindow : 1, 9));
        float stepX = chartW / Math.Max(1, _maxPoints - 1);

        // copy to array
        var tmp = new int[n];
        int i = 0;
        foreach (var hr in _points) tmp[i++] = hr;

        // smoothing window (centered)
        var sm = new float[n];
        if (win == 1)
        {
            for (int k = 0; k < n; k++) sm[k] = tmp[k];
        }
        else
        {
            int half = win / 2;
            for (int k = 0; k < n; k++)
            {
                int a = Math.Max(0, k - half);
                int b = Math.Min(n - 1, k + half);
                float sum = 0;
                int cnt = 0;
                for (int t = a; t <= b; t++) { sum += tmp[t]; cnt++; }
                sm[k] = sum / cnt;
            }
        }

        // build path
        for (int k = 0; k < n; k++)
        {
            float x = left + stepX * k;
            float y = MapY((int)Math.Round(sm[k]), top, chartH);
            if (k == 0) _path.MoveTo(x, y);
            else _path.LineTo(x, y);
        }

        // fill under curve (to X axis)
        _pathFill.AddPath(_path);
        _pathFill.LineTo(left + stepX * (n - 1), top + chartH);
        _pathFill.LineTo(left, top + chartH);
        _pathFill.Close();

        canvas.DrawPath(_pathFill, _lineFill);
        canvas.DrawPath(_path, _line);
    }

    float MapY(int hr, float top, float chartH)
    {
        int clamped = Math.Max(_yMin, Math.Min(_yMax, hr));
        float t = (clamped - _yMin) / (float)(_yMax - _yMin); // 0..1
        return top + (1f - t) * chartH; // invert (higher bpm -> higher on canvas)
    }

    static int RoundUp(int value, int multiple)
    {
        int rem = value % multiple;
        return rem == 0 ? value : value + (multiple - rem);
    }
}
