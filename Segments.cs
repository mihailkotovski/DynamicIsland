using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

public sealed class Segments : FrameworkElement
{
    const double FontSize = 12;
    const double CellPadding = 7, KnobInset = 2;
    const double TrackRadius = 9, KnobRadius = 7;
    const double TrackOpacity = 0.1, KnobOpacity = 0.22;
    const double RestingText = 0.6;
    const double StretchSpeed = 9, StretchWider = 0.18, StretchFlatter = 0.08;
    const double PressedWider = 0.08, PressedTaller = 0.16;
    const double LensZoom = 0.12, GlassShown = 0.01;
    const double RimThickness = 0.8;
    const double SettledShare = 0.15;
    const double ShadowLayers = 2, ShadowSpread = 0.6, PressedShadowSpread = 1.2;
    const double ShadowDrop = 0.7, PressedShadowDrop = 1.1, ShadowAlpha = 0.12, PressedShadowAlpha = 0.05;
    static readonly TimeSpan ShortestPress = TimeSpan.FromMilliseconds(120);

    static readonly DependencyPropertyDescriptor RowPressed =
        DependencyPropertyDescriptor.FromProperty(ButtonBase.IsPressedProperty, typeof(ButtonBase));

    readonly Spring _at = new(0, 520, 30);
    readonly Spring _press = new(0, 900, 38);
    readonly FrameLoop _loop;
    ButtonBase? _row;
    string[] _labels = [];
    int _picked;
    bool _held;
    long _pressedAt;

    public Segments()
    {
        _loop = new FrameLoop(Advance);
        Loaded += (_, _) => AttachToRow();
    }

    public string Labels
    {
        get => string.Join('|', _labels);
        set
        {
            _labels = value.Split('|');
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public void Set(int picked, bool animate)
    {
        _picked = picked;
        if (animate)
        {
            _at.Target = picked;
            _loop.Start();
        }
        else
        {
            _at.Snap(picked);
            InvalidateVisual();
        }
    }

    public int PickUnderPointer()
    {
        Point pointer = Mouse.GetPosition(this);
        bool inside = pointer.X >= 0 && pointer.X < ActualWidth && pointer.Y >= 0 && pointer.Y < ActualHeight;
        if (!inside) return (_picked + 1) % _labels.Length;
        return Math.Clamp((int)((pointer.X - KnobInset) / CellWidth(ActualWidth)), 0, _labels.Length - 1);
    }

    void AttachToRow()
    {
        if (_row != null) return;
        for (DependencyObject? node = VisualTreeHelper.GetParent(this); node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not ButtonBase row) continue;
            _row = row;
            RowPressed.AddValueChanged(row, OnRowPressedChanged);
            return;
        }
    }

    void OnRowPressedChanged(object? sender, EventArgs e)
    {
        if (_row!.IsPressed)
        {
            _held = true;
            _pressedAt = Stopwatch.GetTimestamp();
            _press.Tune(900, 38);
            _press.Target = 1;
            _loop.Start();
        }
        else
        {
            _held = false;
        }
    }

    protected override Size MeasureOverride(Size available)
    {
        double widest = _labels.Length == 0 ? 0 : _labels.Max(label => Format(label, Brushes.White).Width);
        return new Size(Math.Ceiling(widest + CellPadding * 2) * _labels.Length + KnobInset * 2, 0);
    }

    double CellWidth(double width) => (width - KnobInset * 2) / Math.Max(_labels.Length, 1);

    bool Advance(double dt)
    {
        bool settled = Math.Abs(_at.Value - _at.Target) < SettledShare;
        if (!_held && _press.Target == 1 && settled && Stopwatch.GetElapsedTime(_pressedAt) >= ShortestPress)
        {
            _press.Tune(420, 18);
            _press.Target = 0;
        }

        bool moving = _at.Advance(dt) | _press.Advance(dt);
        InvalidateVisual();
        return moving || _press.Target == 1;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0 || _labels.Length == 0) return;

        double cell = CellWidth(w), press = Math.Max(0, _press.Value);
        double stretch = Math.Min(1, Math.Abs(_at.Velocity) / StretchSpeed);
        double knobWidth = cell * (1 + PressedWider * press) * (1 + StretchWider * stretch);
        double knobHeight = (h - KnobInset * 2) * (1 + PressedTaller * press) * (1 - StretchFlatter * stretch);
        double cx = KnobInset + cell * (_at.Value + 0.5), cy = h / 2;

        var trackRect = new Rect(0, 0, w, h);
        Geometry track = Squircle.Of(trackRect, TrackRadius);
        var knobRect = new Rect(cx - knobWidth / 2, cy - knobHeight / 2, knobWidth, knobHeight);
        double kr = Math.Min(KnobRadius * (1 + 0.15 * press), knobHeight / 2);
        Geometry knob = Squircle.Of(knobRect, kr);
        bool glass = press > GlassShown;

        dc.DrawGeometry(Shades.White(TrackOpacity), null, track);
        DrawShadow(dc, knobRect, kr, knob, press);

        if (glass)
        {
            dc.PushClip(knob);
            double zoom = 1 + LensZoom * press;
            dc.PushTransform(new ScaleTransform(zoom, zoom, cx, cy));
            dc.DrawGeometry(Shades.White(TrackOpacity), null, track);
            dc.Pop();
            dc.DrawRectangle(Shades.White(KnobOpacity * (1 - 0.35 * press)), null, knobRect);
            dc.DrawRectangle(Vertical((0, 0.28 * press), (0.48, 0), (1, 0.1 * press)), null, knobRect);
            dc.Pop();
        }
        else
        {
            dc.DrawGeometry(Shades.White(KnobOpacity), null, knob);
            dc.PushClip(knob);
            dc.DrawRectangle(Vertical((0, 0.08), (0.5, 0), (1, 0.03)), null, knobRect);
            dc.Pop();
        }

        double rimTop = 0.24 + 0.56 * press, rimMid = 0.07 + 0.16 * press, rimBot = 0.14 + 0.38 * press;
        var rim = new Pen(Vertical((0, rimTop), (0.5, rimMid), (1, rimBot)), RimThickness);
        Rect rimRect = Rect.Inflate(knobRect, -RimThickness / 2, -RimThickness / 2);
        if (rimRect.Width > 0 && rimRect.Height > 0)
            dc.DrawGeometry(null, rim, Squircle.Of(rimRect, Math.Max(kr - RimThickness / 2, 0)));

        for (int i = 0; i < _labels.Length; i++)
        {
            double near = 1 - Math.Clamp(Math.Abs(_at.Value - i), 0, 1);
            FormattedText label = Format(_labels[i], Shades.White(RestingText + (1 - RestingText) * near));
            dc.DrawText(label, new Point(KnobInset + cell * (i + 0.5) - label.Width / 2, (h - label.Height) / 2));
        }
    }

    static void DrawShadow(DrawingContext dc, Rect knobRect, double kr, Geometry knob, double press)
    {
        double spread = ShadowSpread + PressedShadowSpread * press, drop = ShadowDrop + PressedShadowDrop * press;
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * (ShadowAlpha + PressedShadowAlpha * press)), 0, 0, 0));
        for (int layer = 1; layer <= ShadowLayers; layer++)
        {
            Rect rect = Rect.Inflate(knobRect, spread * layer, spread * layer);
            rect.Offset(0, drop);
            var halo = new RectangleGeometry(rect, kr + spread * layer, kr + spread * layer);
            dc.DrawGeometry(brush, null, Geometry.Combine(halo, knob, GeometryCombineMode.Exclude, null));
        }
    }

    static LinearGradientBrush Vertical(params (double Offset, double Alpha)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach ((double offset, double alpha) in stops) brush.GradientStops.Add(new GradientStop(White(alpha), offset));
        return brush;
    }

    static Color White(double alpha) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), 255, 255, 255);

    FormattedText Format(string text, Brush fill) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface((FontFamily)GetValue(TextElement.FontFamilyProperty), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
        FontSize, fill, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
