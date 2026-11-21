using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

public sealed class Aura : FrameworkElement
{
    const double AttackSeconds = 0.07, ReleaseSeconds = 0.45;
    const double DriftRange = 0.06;
    const double StillBelow = 0.002;
    const double BaseOpacity = 0.16, OpacityGain = 0.5;
    const double BaseWidth = 0.26, WidthGain = 0.1;
    const double BaseHeight = 0.3, HeightGain = 0.4;

    readonly record struct Patch(double Center, double Vertical, double DriftSpeed, double DriftPhase, int PaletteIndex, double Weight);

    static readonly Patch[] Patches =
    [
        new(0.40, 1.00, 0.55, 0.0, 1, 1.0),
        new(0.63, 1.00, 0.43, 2.1, 2, 1.0),
        new(0.15, 1.00, 0.71, 4.0, 0, 1.0),
        new(0.86, 1.00, 0.62, 5.3, 0, 1.0),
        new(0.16, 0.24, 0.37, 1.2, 0, 0.42),
    ];

    static readonly (double Offset, double Alpha)[] Falloff = [(0, 1), (0.25, 0.7), (0.5, 0.3), (0.75, 0.07), (1, 0)];

    readonly RadialGradientBrush[] _brushes = new RadialGradientBrush[Patches.Length];
    readonly double[] _levels = new double[Patches.Length];
    double _time;

    public Aura()
    {
        for (int p = 0; p < _brushes.Length; p++)
        {
            _brushes[p] = new RadialGradientBrush { Opacity = BaseOpacity };
            foreach (var (offset, alpha) in Falloff)
                _brushes[p].GradientStops.Add(new GradientStop(Colors.White.WithAlpha(alpha), offset));
        }
    }

    public void Tint(IReadOnlyList<Color> colors, Duration time)
    {
        for (int p = 0; p < _brushes.Length; p++)
        {
            Color color = colors[Patches[p].PaletteIndex % colors.Count];
            for (int i = 0; i < Falloff.Length; i++)
                _brushes[p].GradientStops[i].BeginAnimation(GradientStop.ColorProperty,
                    new ColorAnimation(color.WithAlpha(Falloff[i].Alpha), time));
        }
    }

    public bool Tick(Equalizer source, double t, double dt)
    {
        double rise = 1 - Math.Exp(-dt / AttackSeconds), fall = 1 - Math.Exp(-dt / ReleaseSeconds);
        bool moved = false;

        for (int p = 0; p < _levels.Length; p++)
        {
            int from = p * source.Bars / _levels.Length, to = Math.Max((p + 1) * source.Bars / _levels.Length, from + 1);
            double target = 0;
            for (int band = from; band < to; band++) target += source.Level(band);
            target /= to - from;

            double next = _levels[p] + (target - _levels[p]) * (target > _levels[p] ? rise : fall);
            if (Math.Abs(next - _levels[p]) > StillBelow) moved = true;
            _levels[p] = next;
            _brushes[p].Opacity = (BaseOpacity + OpacityGain * next) * Patches[p].Weight;
        }

        _time = t;
        if (moved) InvalidateVisual();
        return moved;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        for (int p = 0; p < _brushes.Length; p++)
        {
            Patch patch = Patches[p];
            double level = _levels[p];
            double x = w * (patch.Center + DriftRange * Math.Sin(_time * patch.DriftSpeed + patch.DriftPhase));
            double y = h * patch.Vertical;
            dc.DrawEllipse(_brushes[p], null, new Point(x, y), w * (BaseWidth + WidthGain * level), h * (BaseHeight + HeightGain * level));
        }
    }
}
