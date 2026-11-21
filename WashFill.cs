using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

public sealed class WashFill : FrameworkElement
{
    const double AttackSeconds = 0.08, ReleaseSeconds = 0.42;
    const double WakeSeconds = 0.45, SleepSeconds = 0.85;
    const double ProgressFollow = 0.16;
    const double StillBelow = 0.0015;
    const double WaveAmplitude = 3.2, WaveCalm = 1.1;
    const int WaveSteps = 12;

    readonly SolidColorBrush[] _palette = [new(Colors.White), new(Colors.White), new(Colors.White)];
    readonly double[] _bands = new double[3];
    readonly RadialGradientBrush[] _orbs = new RadialGradientBrush[3];
    readonly RadialGradientBrush _crestGlow = new(Colors.White, Colors.Transparent);
    readonly LinearGradientBrush _baseBrush = new() { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
    readonly LinearGradientBrush _topSheen = CreateTopSheen();
    readonly LinearGradientBrush _shimmer = CreateShimmer();

    double _progress, _shownProgress;
    double _activity, _time;
    bool _active;

    public WashFill()
    {
        IsHitTestVisible = false;
        _palette[0].Changed += (_, _) => InvalidateVisual();

        for (int i = 0; i < 3; i++)
        {
            _orbs[i] = new RadialGradientBrush();
            _orbs[i].GradientStops.Add(new GradientStop(Colors.White, 0));
            _orbs[i].GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        }

        _baseBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        _baseBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.55));
        _baseBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.84));
        _baseBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
    }

    public void Tint(IReadOnlyList<Color> colors, Duration time)
    {
        for (int i = 0; i < _palette.Length; i++)
        {
            Color target = colors[i % colors.Count];
            _palette[i].BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(target, time));
        }
    }

    public bool Tick(Equalizer source, double progress, bool active, bool playing, double t, double dt)
    {
        _active = active;
        _progress = Math.Clamp(progress, 0, 1);

        if (!active)
        {
            _shownProgress = _progress;
            if (_activity > 0)
            {
                _activity = 0;
                InvalidateVisual();
            }
            return false;
        }

        double diff = _progress - _shownProgress;
        if (Math.Abs(diff) > 0.15) _shownProgress = _progress;
        else _shownProgress += diff * (1 - Math.Exp(-dt / ProgressFollow));

        double targetAct = playing ? 1 : 0.25;
        double actRate = 1 - Math.Exp(-dt / (playing ? WakeSeconds : SleepSeconds));
        _activity += (targetAct - _activity) * actRate;

        double rise = 1 - Math.Exp(-dt / AttackSeconds), fall = 1 - Math.Exp(-dt / ReleaseSeconds);
        bool moved = Math.Abs(diff) > StillBelow || playing;

        for (int i = 0; i < _bands.Length; i++)
        {
            int from = i * source.Bars / _bands.Length;
            int to = Math.Max((i + 1) * source.Bars / _bands.Length, from + 1);
            double sum = 0;
            if (playing)
            {
                for (int b = from; b < to; b++) sum += source.Level(b);
                sum /= to - from;
            }
            double next = _bands[i] + (sum - _bands[i]) * (sum > _bands[i] ? rise : fall);
            if (Math.Abs(next - _bands[i]) > StillBelow) moved = true;
            _bands[i] = next;
        }

        _time = t;
        InvalidateVisual();
        return moved;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (!_active || w <= 0 || h <= 0 || _shownProgress <= 0.002) return;

        Color c0 = _palette[0].Color, c1 = _palette[1].Color, c2 = _palette[2].Color;
        double bass = _bands[0], mid = _bands[1], high = _bands[2];
        double energy = 0.55 * bass + 0.30 * mid + 0.15 * high;

        double front = w * _shownProgress;
        double amp = (WaveCalm + WaveAmplitude * (0.4 + 0.85 * bass)) * _activity;

        (Geometry liquid, Geometry crest) = BuildWave(front, h, amp);
        double bodyWidth = Math.Max(front + amp + 4, 1);

        // 1. Multi-color living liquid base
        double alphaBoost = 0.07 * energy;
        _baseBrush.GradientStops[0].Color = c0.WithAlpha(0.15 + alphaBoost);
        _baseBrush.GradientStops[1].Color = c1.WithAlpha(0.24 + alphaBoost * 1.2);
        _baseBrush.GradientStops[2].Color = Brighten(c2, 0.16).WithAlpha(0.32 + alphaBoost * 1.4);
        _baseBrush.GradientStops[3].Color = Brighten(c2, 0.30).WithAlpha(0.38 + alphaBoost * 1.5);

        dc.PushClip(liquid);

        dc.DrawRectangle(_baseBrush, null, new Rect(0, 0, bodyWidth, h));

        // 2. Drifting audio-reactive caustic orbs inside the liquid
        double[] anchors = [0.22, 0.56, 0.86];
        double[] speeds = [0.85, 1.15, 1.45];
        double[] phases = [0.0, 2.2, 4.4];
        Color[] orbColors = [c0, c1, Brighten(c2, 0.28)];

        for (int i = 0; i < 3; i++)
        {
            double level = _bands[i];
            double cx = Math.Min(front * anchors[i] + 10 * Math.Sin(_time * speeds[i] + phases[i]), front + 4);
            double cy = h * (0.5 + 0.28 * Math.Cos(_time * (speeds[i] * 0.77) + phases[i] + 1.1));
            double rx = 24 + 22 * level + 0.08 * front;
            double ry = h * (0.62 + 0.35 * level);
            double op = (0.14 + 0.28 * level) * (0.45 + 0.55 * _activity);

            _orbs[i].GradientStops[0].Color = orbColors[i].WithAlpha(op);
            _orbs[i].GradientStops[1].Color = orbColors[i].WithAlpha(0);
            dc.DrawEllipse(_orbs[i], null, new Point(cx, cy), rx, ry);
        }

        // 3. Soft travelling shimmer highlight across the filled liquid
        if (_activity > 0.1 && front > 24)
        {
            double sweep = (_time * 0.28) % 1.6 - 0.3;
            double sx = sweep * front;
            double sw = Math.Clamp(front * 0.36, 28, 72);
            dc.PushOpacity(0.12 * _activity * (0.65 + 0.55 * mid));
            dc.DrawRectangle(_shimmer, null, new Rect(sx - sw / 2, 0, sw, h));
            dc.Pop();
        }

        // 4. Subtle glass top/bottom internal sheen along the liquid
        dc.PushOpacity(0.14 + 0.08 * energy);
        dc.DrawRectangle(_topSheen, null, new Rect(0, 0, bodyWidth, h));
        dc.Pop();

        dc.Pop();

        // 5. Luminous caustic aura spilling smoothly ahead of the wave crest
        double crestY = h * (0.5 + 0.18 * Math.Sin(_time * 1.9));
        double crestAlpha = (0.30 + 0.30 * energy) * (0.55 + 0.45 * _activity);
        _crestGlow.GradientStops[0].Color = Brighten(c2, 0.45).WithAlpha(crestAlpha);
        _crestGlow.GradientStops[1].Color = c2.WithAlpha(0);
        dc.DrawEllipse(_crestGlow, null, new Point(front, crestY), 18 + 10 * bass, h * 0.78);

        // 6. Delicate luminous meniscus highlight along the wave front
        if (front > 4 && front < w - 2)
        {
            Color edgeColor = Brighten(c2, 0.55);
            var haloPen = new Pen(Shades.Frozen(new SolidColorBrush(edgeColor.WithAlpha(0.22 + 0.18 * energy))), 4.5);
            var crestPen = new Pen(Shades.Frozen(new SolidColorBrush(edgeColor.WithAlpha(0.45 + 0.3 * energy))), 1.2);
            dc.DrawGeometry(null, haloPen, crest);
            dc.DrawGeometry(null, crestPen, crest);
        }
    }

    (Geometry Body, Geometry Crest) BuildWave(double front, double h, double amp)
    {
        var pts = new Point[WaveSteps + 1];
        for (int i = 0; i <= WaveSteps; i++)
        {
            double v = (double)i / WaveSteps;
            pts[i] = new Point(Math.Max(front + WaveOffset(v, amp), 0), v * h);
        }

        var body = new StreamGeometry();
        using (StreamGeometryContext g = body.Open())
        {
            g.BeginFigure(new Point(0, 0), true, true);
            g.LineTo(pts[0], false, false);
            for (int i = 1; i <= WaveSteps; i++) g.LineTo(pts[i], false, false);
            g.LineTo(new Point(0, h), false, false);
        }
        body.Freeze();

        var crest = new StreamGeometry();
        using (StreamGeometryContext g = crest.Open())
        {
            g.BeginFigure(pts[0], false, false);
            for (int i = 1; i <= WaveSteps; i++) g.LineTo(pts[i], true, true);
        }
        crest.Freeze();

        return (body, crest);
    }

    double WaveOffset(double v, double amp) =>
        amp * (Math.Sin(v * Math.PI * 1.8 - _time * 3.4) * 0.65 + Math.Cos(v * Math.PI * 3.1 + _time * 2.1) * 0.35);

    static Color Brighten(Color c, double share) => Color.FromRgb(
        (byte)Math.Round(c.R + (255 - c.R) * share),
        (byte)Math.Round(c.G + (255 - c.G) * share),
        (byte)Math.Round(c.B + (255 - c.B) * share));

    static LinearGradientBrush CreateTopSheen()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(Colors.White, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.36));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.74));
        brush.GradientStops.Add(new GradientStop(Colors.White.WithAlpha(0.45), 1));
        brush.Freeze();
        return brush;
    }

    static LinearGradientBrush CreateShimmer()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.25) };
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        brush.GradientStops.Add(new GradientStop(Colors.White, 0.5));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        brush.Freeze();
        return brush;
    }
}
