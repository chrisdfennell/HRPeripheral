using Android.Content;
using Android.Graphics;
using Android.Views;
using System.Collections.Generic;
// Disambiguate Path:
using APath = Android.Graphics.Path;

namespace HRPeripheral;

public class HrGraphView : View
{
    readonly Queue<int> _points = new Queue<int>();
    const int MaxPoints = 120; // ~2 minutes at ~1Hz

    readonly Paint _axis = new Paint()
    {
        StrokeWidth = 2,
        Color = Color.Gray,
        AntiAlias = true
    };

    readonly Paint _line = new Paint()
    {
        StrokeWidth = 4,
        Color = Color.White,
        AntiAlias = true
        // Style can't be set inside initializer; do it in ctor
    };

    public HrGraphView(Context c) : base(c)
    {
        SetBackgroundColor(Color.Black);
        _line.SetStyle(Paint.Style.Stroke);
    }

    public void Push(int hr)
    {
        if (_points.Count >= MaxPoints) _points.Dequeue();
        _points.Enqueue(hr);
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var w = Width; var h = Height;
        if (w <= 0 || h <= 0) return;

        // Axes
        canvas.DrawLine(0, h - 1, w, h - 1, _axis);
        canvas.DrawLine(0, 0, 0, h, _axis);

        if (_points.Count < 2) return;

        // Map HR (50..190 bpm) to Y
        float MapY(int hr)
        {
            var clamped = System.Math.Max(50, System.Math.Min(190, hr));
            float t = (clamped - 50) / 140f; // 0..1
            return h - 1 - (t * (h - 4));    // invert for canvas
        }

        var step = w / (float)(MaxPoints - 1);
        var path = new APath();
        int i = 0;
        foreach (var hr in _points)
        {
            var x = i * step;
            var y = MapY(hr);
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
            i++;
        }
        canvas.DrawPath(path, _line);
    }
}