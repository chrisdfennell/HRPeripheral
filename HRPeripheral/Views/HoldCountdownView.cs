using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;

namespace HRPeripheral.Views
{
    public class HoldCountdownView : View
    {
        private readonly Paint _bgPaint;
        private readonly Paint _fgPaint;
        private readonly Paint _textPaint;
        private float _progress; // 0..1

        public HoldCountdownView(Context context) : base(context)
        {
            _bgPaint = new Paint(PaintFlags.AntiAlias)
            {
                Color = new Color(255, 255, 255, 50),
                StrokeWidth = 10f,
                StrokeCap = Paint.Cap.Round
            };
            _bgPaint.SetStyle(Paint.Style.Stroke);

            _fgPaint = new Paint(PaintFlags.AntiAlias)
            {
                Color = new Color(255, 255, 255, 220),
                StrokeWidth = 10f,
                StrokeCap = Paint.Cap.Round
            };
            _fgPaint.SetStyle(Paint.Style.Stroke);

            _textPaint = new Paint(PaintFlags.AntiAlias)
            {
                Color = new Color(255, 255, 255, 220),
                TextSize = 28f,
                TextAlign = Paint.Align.Center
            };

            Visibility = ViewStates.Gone;
        }

        public HoldCountdownView(Context context, IAttributeSet attrs) : this(context) { }

        public void SetProgress(float p) // 0..1
        {
            if (p < 0f) p = 0f;
            if (p > 1f) p = 1f;
            _progress = p;
            Invalidate();
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            var w = Width;
            var h = Height;
            var size = System.Math.Min(w, h) * 0.65f;
            var cx = w / 2f;
            var cy = h / 2f;
            var r = size / 2f;

            var rect = new RectF(cx - r, cy - r, cx + r, cy + r);

            // background ring
            canvas.DrawArc(rect, -90, 360, false, _bgPaint);

            // progress ring
            canvas.DrawArc(rect, -90, 360f * _progress, false, _fgPaint);

            // center “X”
            canvas.DrawText("✕", cx, cy + (_textPaint.TextSize * 0.35f), _textPaint);
        }
        protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
        {
            // prefer square
            int size = System.Math.Min(MeasureSpec.GetSize(widthMeasureSpec), MeasureSpec.GetSize(heightMeasureSpec));
            SetMeasuredDimension(size, size);
        }
    }
}
