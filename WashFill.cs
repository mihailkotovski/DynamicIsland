using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

public sealed class WashFill : FrameworkElement
{
    const double AttackSeconds = 0.09, ReleaseSeconds = 0.55;
    const double WakeSeconds = 0.45, SleepSeconds = 0.85;
    const double ProgressFollow = 0.16, SnapJump = 0.15;
    const double StillBelow = 0.0015, ProgressStill = 0.0005;
    const double PausedActivity = 0.25, PausedShare = 0.55;
    const double IntroSpan = 0.06, OutroSpan = 0.08;
    const double TrailOpacity = 0.075, TrailFeather = 44;
    const double SpotOpacity = 0.21, SpotBeatGain = 0.045;
    const double SpotMix = 0.65, SpotRadiusX = 56, SpotRadiusYRatio = 0.79, SpotVertical = 0.54;

    static readonly (double Offset, double Alpha)[] TrailFalloff = [(0, 1), (0.35, 0.65), (0.7, 0.2), (1, 0)];
    static readonly (double Offset, double Alpha)[] SpotFalloff = [(0, 1), (0.25, 0.7), (0.5, 0.3), (0.75, 0.07), (1, 0)];

    readonly LinearGradientBrush _trail = new() { MappingMode = BrushMappingMode.Absolute };
    readonly RadialGradientBrush _spot = new();

    double _progress, _shownProgress, _renderedProgress;
    double _activity, _bass;
    bool _active;

    public WashFill()
    {
        IsHitTestVisible = false;
        foreach (var (offset, alpha) in TrailFalloff)
            _trail.GradientStops.Add(new GradientStop(Colors.White.WithAlpha(alpha), offset));
        foreach (var (offset, alpha) in SpotFalloff)
            _spot.GradientStops.Add(new GradientStop(Colors.White.WithAlpha(alpha), offset));
    }

    public void Tint(IReadOnlyList<Color> colors, Duration time)
    {
        if (colors.Count == 0) return;
        Color trail = colors[0];
        Color spot = Mix(trail, colors[Math.Min(1, colors.Count - 1)], SpotMix);

        for (int i = 0; i < TrailFalloff.Length; i++)
            _trail.GradientStops[i].BeginAnimation(GradientStop.ColorProperty,
                new ColorAnimation(trail.WithAlpha(TrailFalloff[i].Alpha), time));
        for (int i = 0; i < SpotFalloff.Length; i++)
            _spot.GradientStops[i].BeginAnimation(GradientStop.ColorProperty,
                new ColorAnimation(spot.WithAlpha(SpotFalloff[i].Alpha), time));
    }

    public bool Tick(Equalizer source, double progress, bool active, bool playing, double t, double dt)
    {
        bool wasActive = _active;
        _active = active;
        _progress = Math.Clamp(progress, 0, 1);

        if (!active)
        {
            _shownProgress = _renderedProgress = _progress;
            _bass = 0;
            if (wasActive || _activity > 0)
            {
                _activity = 0;
                InvalidateVisual();
            }
            return false;
        }

        double diff = _progress - _shownProgress;
        if (!wasActive || Math.Abs(diff) > SnapJump) _shownProgress = _progress;
        else _shownProgress += diff * (1 - Math.Exp(-dt / ProgressFollow));

        double targetAct = playing ? 1 : PausedActivity;
        double nextAct = _activity + (targetAct - _activity) * (1 - Math.Exp(-dt / (playing ? WakeSeconds : SleepSeconds)));

        double bassTarget = 0;
        if (playing)
        {
            int count = Math.Max(1, source.Bars / 3);
            for (int b = 0; b < count; b++) bassTarget += source.Level(b);
            bassTarget /= count;
        }
        double nextBass = _bass + (bassTarget - _bass) * (1 - Math.Exp(-dt / (bassTarget > _bass ? AttackSeconds : ReleaseSeconds)));

        bool redraw = !wasActive
            || Math.Abs(_shownProgress - _renderedProgress) > ProgressStill
            || Math.Abs(nextAct - _activity) > StillBelow
            || Math.Abs(nextBass - _bass) > StillBelow;
        bool moved = redraw || Math.Abs(_progress - _shownProgress) > ProgressStill;

        _activity = nextAct;
        _bass = nextBass;
        if (redraw) InvalidateVisual();
        return moved;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (!_active || w <= 0 || h <= 0 || _shownProgress <= StillBelow) return;

        double p = _shownProgress;
        _renderedProgress = p;
        double front = w * p;
        double intro = Math.Clamp(p / IntroSpan, 0, 1);
        double outro = Math.Clamp((1 - p) / OutroSpan, 0, 1);
        double act = (PausedShare + (1 - PausedShare) * _activity) * intro;

        double fadeCenter = front + TrailFeather * 0.65 * (1 - outro);
        _trail.StartPoint = new Point(fadeCenter - TrailFeather * 0.58, 0);
        _trail.EndPoint = new Point(fadeCenter + TrailFeather * 0.42, 0);
        _trail.Opacity = TrailOpacity * act;
        dc.DrawRectangle(_trail, null, new Rect(0, 0, w, h));

        _spot.Opacity = Math.Clamp((SpotOpacity + SpotBeatGain * _bass) * act, 0, 1);
        dc.DrawEllipse(_spot, null, new Point(front, h * SpotVertical), SpotRadiusX, h * SpotRadiusYRatio);
    }

    static Color Mix(Color a, Color b, double share) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * share),
        (byte)Math.Round(a.G + (b.G - a.G) * share),
        (byte)Math.Round(a.B + (b.B - a.B) * share));
}
