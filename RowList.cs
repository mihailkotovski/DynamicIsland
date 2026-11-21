using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

public sealed class RowList : StackPanel
{
    const double Radius = 12;
    const double HoverOpacity = 0.12, PressOpacity = 0.21;
    const double PressInsetX = 5, PressInsetY = 2;
    const double AppearSquish = 0.6;
    const double HiddenBelow = 0.05;
    const double RimThickness = 0.8;

    static readonly Brush RimBrush = CreateRimBrush();

    readonly SolidColorBrush _fill = new(Colors.White) { Opacity = 0 };
    readonly Spring _top = new(0), _bottom = new(0);
    readonly Spring _shown = new(0), _squish = new(0);
    readonly FrameLoop _loop;
    Rect _rect = Rect.Empty;
    bool _lit, _pressed;

    public RowList()
    {
        _loop = new FrameLoop(Advance);
        AddHandler(Mouse.MouseMoveEvent, new MouseEventHandler((_, _) => UpdateHighlight()), true);
        AddHandler(Mouse.MouseDownEvent, new MouseButtonEventHandler((_, _) => UpdateHighlight()), true);
        AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler((_, _) => UpdateHighlight()), true);
        AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler((_, _) => UpdateHighlight()), true);
        MouseEnter += (_, _) => UpdateHighlight();
        MouseLeave += (_, _) => UpdateHighlight();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) Reset();
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double shown = Math.Clamp(_shown.Value, 0, 1);
        if (shown < 0.005 || _rect.Width <= 1 || _rect.Height <= 1) return;

        Geometry pill = Squircle.Of(_rect, Radius);
        dc.DrawGeometry(_fill, null, pill);

        Rect rimRect = Rect.Inflate(_rect, -RimThickness / 2, -RimThickness / 2);
        if (rimRect.Width <= 0 || rimRect.Height <= 0) return;

        dc.PushOpacity(shown);
        var pen = new Pen(RimBrush, RimThickness);
        dc.DrawGeometry(null, pen, Squircle.Of(rimRect, Math.Max(Radius - RimThickness / 2, 0)));
        dc.Pop();
    }

    public void FollowPointer()
    {
        Mouse.Synchronize();
        UpdateHighlight();
    }

    void UpdateHighlight()
    {
        ButtonBase? row = InternalChildren.OfType<ButtonBase>().FirstOrDefault(button => button.IsMouseOver);
        if (row != null) MoveTo(row);

        bool lit = row != null, pressed = row is { IsPressed: true };
        if (lit != _lit) _shown.Tune(lit ? 420 : 90, lit ? 41 : 19);
        if (pressed != _pressed) _squish.Tune(pressed ? 700 : 380, pressed ? 44 : 16);
        _lit = lit;
        _pressed = pressed;
        _shown.Target = lit ? 1 : 0;
        _squish.Target = pressed ? 1 : 0;
        _loop.Start();
    }

    void MoveTo(ButtonBase row)
    {
        double top = VisualTreeHelper.GetOffset(row).Y, bottom = top + row.RenderSize.Height;
        if (_shown.Value < HiddenBelow)
        {
            _top.Snap(top);
            _bottom.Snap(bottom);
            _squish.Value = AppearSquish;
            return;
        }

        if (top != _top.Target)
        {
            bool down = top > _top.Target;
            (down ? _bottom : _top).Tune(560, 36);
            (down ? _top : _bottom).Tune(230, 26);
        }
        _top.Target = top;
        _bottom.Target = bottom;
    }

    void Reset()
    {
        _lit = _pressed = false;
        _shown.Snap(0);
        _squish.Snap(0);
        ApplyHighlight();
    }

    bool Advance(double dt)
    {
        bool moving = _top.Advance(dt);
        moving |= _bottom.Advance(dt);
        moving |= _shown.Advance(dt);
        moving |= _squish.Advance(dt);
        ApplyHighlight();
        return moving;
    }

    void ApplyHighlight()
    {
        double x = PressInsetX * _squish.Value, y = PressInsetY * _squish.Value;
        _rect = new Rect(x, _top.Value + y,
            Math.Max(ActualWidth - x * 2, 0), Math.Max(_bottom.Value - _top.Value - y * 2, 0));
        _fill.Opacity = Math.Clamp(_shown.Value, 0, 1)
            * (HoverOpacity + (PressOpacity - HoverOpacity) * Math.Clamp(_squish.Value, 0, 1));
        InvalidateVisual();
    }

    static Brush CreateRimBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 1));
        brush.Freeze();
        return brush;
    }
}
