using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

public sealed class TempSparkline : FrameworkElement
{
    const double PaddingTop = 5, PaddingBottom = 5;
    const double LineThickness = 1.6, DotRadius = 2.4, GlowRadius = 5.5;
    const double FillOpacity = 0.22;

    int[] _degrees = [];
    Color _accent = Color.FromRgb(0x64, 0xD2, 0xFF);

    public TempSparkline() => IsHitTestVisible = false;

    public void SetData(int[] degrees, Color accent)
    {
        _degrees = degrees;
        _accent = accent;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0 || _degrees.Length < 2) return;

        int n = _degrees.Length;
        int min = _degrees[0], max = _degrees[0];
        for (int i = 1; i < n; i++)
        {
            if (_degrees[i] < min) min = _degrees[i];
            if (_degrees[i] > max) max = _degrees[i];
        }

        double span = Math.Max(max - min, 2);
        double usable = Math.Max(h - PaddingTop - PaddingBottom, 1);
        double cell = w / n;
        var points = new Point[n];
        for (int i = 0; i < n; i++)
        {
            double x = cell * (i + 0.5);
            double norm = (_degrees[i] - min) / span;
            double y = PaddingTop + (1 - norm) * usable;
            points[i] = new Point(x, y);
        }

        var curve = new StreamGeometry();
        using (StreamGeometryContext g = curve.Open())
        {
            g.BeginFigure(points[0], false, false);
            for (int i = 0; i < n - 1; i++)
            {
                Point p0 = points[i], p1 = points[i + 1];
                double midX = (p0.X + p1.X) / 2;
                g.BezierTo(new Point(midX, p0.Y), new Point(midX, p1.Y), p1, true, true);
            }
        }
        curve.Freeze();

        var area = new StreamGeometry();
        using (StreamGeometryContext g = area.Open())
        {
            g.BeginFigure(new Point(points[0].X, h), true, true);
            g.LineTo(points[0], false, false);
            for (int i = 0; i < n - 1; i++)
            {
                Point p0 = points[i], p1 = points[i + 1];
                double midX = (p0.X + p1.X) / 2;
                g.BezierTo(new Point(midX, p0.Y), new Point(midX, p1.Y), p1, false, false);
            }
            g.LineTo(new Point(points[^1].X, h), false, false);
        }
        area.Freeze();

        var fill = new LinearGradientBrush(_accent.WithAlpha(FillOpacity), _accent.WithAlpha(0), new Point(0, 0), new Point(0, 1));
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);

        var lineBrush = new LinearGradientBrush(_accent.WithAlpha(0.55), _accent.WithAlpha(0.95), new Point(0, 0), new Point(1, 0));
        lineBrush.GradientStops.Add(new GradientStop(_accent.WithAlpha(0.55), 1));
        lineBrush.Freeze();
        var pen = new Pen(lineBrush, LineThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, curve);

        var glow = new RadialGradientBrush(_accent.WithAlpha(0.45), _accent.WithAlpha(0));
        glow.Freeze();
        var dotFill = Shades.Frozen(new SolidColorBrush(Colors.White));
        var dotRing = new Pen(Shades.Frozen(new SolidColorBrush(_accent)), 1.2);
        dotRing.Freeze();

        for (int i = 0; i < n; i++)
        {
            dc.DrawEllipse(glow, null, points[i], GlowRadius, GlowRadius);
            dc.DrawEllipse(dotFill, dotRing, points[i], DotRadius, DotRadius);
        }
    }
}
