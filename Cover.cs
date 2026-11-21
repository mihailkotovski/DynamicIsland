using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public sealed class Cover : Grid
{
    const double FirstScale = 0.88;
    const double RimThickness = 1;
    static readonly Duration SlideTime = Ms(460);
    static readonly Brush RimBrush = CreateRimBrush();

    readonly Grid _layers = new();
    readonly Path _rim = new()
    {
        Stroke = RimBrush,
        StrokeThickness = RimThickness,
        IsHitTestVisible = false,
    };
    Border? _shown;

    public Cover()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Children.Add(_layers);
        Children.Add(_rim);
    }

    public double Radius { get; set; }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        var box = new Rect(info.NewSize);
        Clip = Squircle.Of(box, Radius);
        Rect inset = box;
        inset.Inflate(-RimThickness / 2, -RimThickness / 2);
        _rim.Data = inset.Width > 0 && inset.Height > 0
            ? Squircle.Of(inset, Math.Max(Radius - RimThickness / 2, 0))
            : Geometry.Empty;
    }

    public void Show(ImageSource? art, int direction)
    {
        Border? old = _shown;
        Border? next = _shown = art != null ? CreateLayer(art) : null;
        if (!IsVisible || ActualWidth <= 0)
        {
            _layers.Children.Clear();
            if (next != null) _layers.Children.Add(next);
            return;
        }

        double shift = ActualWidth * (direction < 0 ? -1 : 1);
        var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
        if (old != null)
        {
            var leave = new DoubleAnimation(-shift, SlideTime) { EasingFunction = ease };
            leave.Completed += (_, _) => _layers.Children.Remove(old);
            Offset(old).BeginAnimation(TranslateTransform.XProperty, leave);
            if (next == null) old.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(240)));
        }
        if (next == null) return;

        _layers.Children.Add(next);
        if (old != null)
        {
            Offset(next).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(shift, 0, SlideTime) { EasingFunction = ease });
            return;
        }

        Scale(next).AnimateScale(new DoubleAnimation(FirstScale, 1, SlideTime) { EasingFunction = ease });
        next.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(300)));
    }

    static Brush CreateRimBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 1));
        brush.Freeze();
        return brush;
    }

    static Border CreateLayer(ImageSource art) => new()
    {
        Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill },
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } },
    };

    static Transform Scale(Border layer) => ((TransformGroup)layer.RenderTransform).Children[0];

    static Transform Offset(Border layer) => ((TransformGroup)layer.RenderTransform).Children[1];
}
