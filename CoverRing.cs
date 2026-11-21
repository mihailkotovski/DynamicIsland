using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

public sealed class CoverRing : FrameworkElement
{
    const double StrokeThickness = 1.6;
    const double GlowThickness = 3.6;
    const double TrackOpacity = 0.2;
    const double GlowOpacity = 0.28;
    const double Empty = 0.002, Full = 0.998;
    const double FlattenTolerance = 0.15;

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(SolidColorBrush), typeof(CoverRing),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    double _progress;
    double _radius = 7.5;
    Size _cachedSize;
    Geometry? _track;
    Point[] _poly = [];
    double[] _cumLen = [];
    double _totalLen;

    public CoverRing() => IsHitTestVisible = false;

    public SolidColorBrush Accent
    {
        get => (SolidColorBrush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public double Radius
    {
        get => _radius;
        set
        {
            if (value == _radius) return;
            _radius = value;
            _cachedSize = default;
            InvalidateVisual();
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(value - _progress) < 0.0005) return;
            _progress = value;
            InvalidateVisual();
        }
    }

    void EnsureGeometry(double w, double h)
    {
        var size = new Size(w, h);
        if (_track != null && _cachedSize == size) return;
        _cachedSize = size;

        double inset = StrokeThickness / 2 + 0.5;
        var rect = new Rect(inset, inset, Math.Max(w - 2 * inset, 1), Math.Max(h - 2 * inset, 1));
        _track = Squircle.Of(rect, Math.Min(_radius, Math.Min(rect.Width, rect.Height) / 2));

        PathGeometry flat = _track.GetFlattenedPathGeometry(FlattenTolerance, ToleranceType.Absolute);
        var pts = new List<Point>(128);
        if (flat.Figures.Count > 0)
        {
            foreach (Point pt in flat.Figures[0].Points()) pts.Add(pt);
            if (pts.Count > 1 && pts[^1] != pts[0]) pts.Add(pts[0]);
        }

        _poly = [.. pts];
        _cumLen = new double[_poly.Length];
        double total = 0;
        for (int i = 1; i < _poly.Length; i++)
        {
            total += (_poly[i] - _poly[i - 1]).Length;
            _cumLen[i] = total;
        }
        _totalLen = total;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        EnsureGeometry(w, h);
        if (_track == null) return;

        var trackPen = new Pen(Shades.White(TrackOpacity), StrokeThickness) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, trackPen, _track);

        if (_progress <= Empty || _totalLen <= 0) return;

        Geometry active = _progress >= Full ? _track : Slice(_progress * _totalLen);
        Color color = Accent.Color;

        var glowPen = new Pen(Shades.Frozen(new SolidColorBrush(color.WithAlpha(GlowOpacity))), GlowThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        var linePen = new Pen(Shades.Frozen(new SolidColorBrush(color)), StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        dc.DrawGeometry(null, glowPen, active);
        dc.DrawGeometry(null, linePen, active);
    }

    Geometry Slice(double targetLen)
    {
        var arc = new StreamGeometry();
        using (StreamGeometryContext g = arc.Open())
        {
            g.BeginFigure(_poly[0], false, false);
            for (int i = 1; i < _poly.Length; i++)
            {
                if (_cumLen[i] < targetLen)
                {
                    g.LineTo(_poly[i], true, true);
                    continue;
                }
                double seg = _cumLen[i] - _cumLen[i - 1];
                double frac = seg > 0 ? (targetLen - _cumLen[i - 1]) / seg : 0;
                Point end = _poly[i - 1] + (_poly[i] - _poly[i - 1]) * frac;
                g.LineTo(end, true, true);
                break;
            }
        }
        arc.Freeze();
        return arc;
    }
}
