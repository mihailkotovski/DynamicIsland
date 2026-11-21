using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public sealed class CoverBackdrop : Grid
{
    const double BlurRadius = 54;
    const double BaseScale = 1.55, BreathScale = 0.08, BeatScale = 0.09;
    const double BaseOpacity = 0.42, BeatOpacity = 0.16;
    const double AttackSeconds = 0.09, ReleaseSeconds = 0.55;
    const double StillBelow = 0.002;
    const double DriftX = 14, DriftY = 10;
    const double SpinSpeed = 3.2;
    static readonly Duration CrossfadeTime = Ms(520);

    readonly Grid _layers = new()
    {
        RenderTransformOrigin = new Point(0.5, 0.5),
        Effect = new BlurEffect { Radius = BlurRadius, RenderingBias = RenderingBias.Performance },
    };
    readonly ScaleTransform _scale = new(BaseScale, BaseScale);
    readonly RotateTransform _rotate = new(0);
    readonly TranslateTransform _drift = new(0, 0);
    readonly Border _scrim;

    Border? _shown;
    ImageSource? _art;
    double _level, _angle, _time;

    public CoverBackdrop()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(_layers, BitmapScalingMode.LowQuality);

        _layers.RenderTransform = new TransformGroup
        {
            Children = { _scale, _rotate, _drift },
        };
        _layers.Opacity = BaseOpacity;

        var veil = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        veil.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 0, 0, 0), 0));
        veil.GradientStops.Add(new GradientStop(Color.FromArgb(0x8A, 0, 0, 0), 0.55));
        veil.GradientStops.Add(new GradientStop(Color.FromArgb(0xB8, 0, 0, 0), 1));
        veil.Freeze();

        _scrim = new Border { Background = veil, IsHitTestVisible = false };

        Children.Add(_layers);
        Children.Add(_scrim);
    }

    public void SetArt(ImageSource? art, bool animate)
    {
        if (ReferenceEquals(_art, art)) return;
        _art = art;

        Border? old = _shown;
        Border? next = _shown = art != null
            ? new Border { Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill } }
            : null;

        if (!animate || !IsVisible)
        {
            _layers.Children.Clear();
            if (next != null) _layers.Children.Add(next);
            return;
        }

        if (next != null)
        {
            next.Opacity = 0;
            _layers.Children.Add(next);
            next.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, CrossfadeTime));
        }
        if (old == null) return;

        var fade = new DoubleAnimation(0, CrossfadeTime);
        fade.Completed += (_, _) => _layers.Children.Remove(old);
        old.BeginAnimation(OpacityProperty, fade);
    }

    public bool Tick(Equalizer source, bool playing, double t, double dt)
    {
        double target = 0;
        if (playing)
        {
            int count = Math.Min(3, source.Bars);
            for (int i = 0; i < count; i++) target += source.Level(i);
            target /= Math.Max(count, 1);
        }

        double rise = 1 - Math.Exp(-dt / AttackSeconds), fall = 1 - Math.Exp(-dt / ReleaseSeconds);
        double next = _level + (target - _level) * (target > _level ? rise : fall);
        bool moved = Math.Abs(next - _level) > StillBelow || (playing && _art != null);
        _level = next;
        _time = t;

        if (playing && _art != null) _angle = (_angle + SpinSpeed * dt) % 360;

        double pulse = BaseScale + BreathScale * Math.Sin(_time * 0.45) + BeatScale * _level;
        _scale.ScaleX = _scale.ScaleY = pulse;
        _rotate.Angle = _angle;
        _drift.X = DriftX * Math.Sin(_time * 0.31);
        _drift.Y = DriftY * Math.Cos(_time * 0.27);
        _layers.Opacity = BaseOpacity + BeatOpacity * _level;

        return moved;
    }
}
