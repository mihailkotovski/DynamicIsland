using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

public sealed class LineBar : FrameworkElement
{
    const double MarkGap = 2;
    const double MinSegmentWidth = 6;
    const double SnapReach = 5;
    const double PassedOpacity = 0.85;
    const double AheadOpacity = 0.3;
    const double UnplayedOpacity = 0.2;
    const double SampleStep = 4;
    const double MinThickness = 2, RestThickness = 6;
    const double BulgeThickness = 8, BulgeReach = 28;
    const double LiftThickness = 12, LitAheadOpacity = 0.45, DimmedShare = 0.45;
    const double WaveThickness = 10, WaveSpeed = 800;
    const double TipFontSize = 12, TipPadding = 10, TipHeight = 22, TipLift = 4, TipRise = 4;
    const int GlowSpriteWidth = 256, GlowSpriteHeight = 128;
    const double GlowTipOffset = 0.96;
    const double GlowMaxTail = 136, GlowFadeIn = 32, GlowHalfHeight = 38;
    const double GlowPausedAlpha = 0.38, GlowWakeSeconds = 0.35, GlowSleepSeconds = 0.75;
    const double GlowBassAttack = 0.07, GlowBassRelease = 0.38;
    const double GlowSpeedScale = 700, GlowFwdTailBoost = 0.42, GlowRevTailShrink = 0.45;
    const double GlowFwdAlphaBoost = 0.22, GlowRevAlphaDrop = 0.35, GlowFwdNarrow = 0.1;
    const double GlowBassAlpha = 0.18, GlowBassSpread = 0.15, GlowBassTail = 0.08;
    const double MaxBow = 9, BowResist = 28, BowClipPad = 32;
    const double DetentImpulse = 120;
    const double CapTolerance = 0.05;

    static readonly Brush TipBack = Shades.Frozen(new SolidColorBrush(Color.FromArgb(0xF0, 0x24, 0x24, 0x24)));
    static readonly BitmapSource GlowSprite = CreateGlowSprite();

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(SolidColorBrush), typeof(LineBar),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    sealed class Column(double left, double right, string? text)
    {
        public double Left { get; } = left;
        public double Right { get; } = right;
        public string? Text { get; } = text;
        public double Center => (Left + Right) / 2;
        public Spring Rise { get; } = new(RestThickness);
        public Spring Detent { get; } = new(0, 520, 22);
        public bool Raised { get; set; }
    }

    readonly record struct Ripple(double At, double From, bool Swelling);

    readonly Shades _shades = new();
    readonly ImageBrush _glowMask = new(GlowSprite) { Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute };
    readonly List<Column> _columns = [];
    readonly List<Ripple> _ripples = [];
    readonly Spring _focus = new(0, 300, 30), _tipShown = new(0, 300, 30), _tipX = new(0, 420, 32);
    readonly Spring _scrubBlend = new(0, 480, 32), _glowSpeed = new(0, 260, 22);
    readonly Spring _bowY = new(0, 320, 17), _bowX = new(0, 380, 26);

    double[] _starts = [];
    string[] _texts = [];
    double _fill, _thickness = RestThickness;
    double _columnsWidth = -1;
    bool _columnsStale = true;
    SeekHover _hover = SeekHover.Magnifier;
    double _time, _lastX;
    double _glowActivity = 1, _glowBass;
    bool _pointed, _swollen, _glow = true;
    int _pointedColumn = -1, _lastFillColumn = -1;
    string _tipText = "";

    public SolidColorBrush Accent
    {
        get => (SolidColorBrush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public bool Solid { get; set; }

    public bool Glow
    {
        get => _glow;
        set
        {
            if (value == _glow) return;
            _glow = value;
            InvalidateVisual();
        }
    }

    public bool HasMarks => _starts.Length > 0;

    public double Fill
    {
        get => _fill;
        set
        {
            if (value == _fill) return;
            _fill = value;
            InvalidateVisual();
        }
    }

    public double Thickness
    {
        get => _thickness;
        set
        {
            if (value == _thickness) return;
            _thickness = value;
            InvalidateVisual();
        }
    }

    public void SetLines(double[] starts, string[] texts)
    {
        _starts = starts;
        _texts = texts;
        _columnsStale = true;
        InvalidateVisual();
    }

    public double Snap(double x)
    {
        List<double> edges = Edges(ActualWidth);
        double nearest = x;
        for (int i = 1; i < edges.Count - 1; i++)
            if (Math.Abs(edges[i] - x) <= SnapReach && Math.Abs(edges[i] - x) < Math.Abs(nearest - x)) nearest = edges[i];
        return nearest;
    }

    internal void Advance(double dt, Point? pointer, bool scrubbing, SeekHover hover, double fillVelocity, bool playing, double bass)
    {
        double width = ActualWidth;
        if (width <= 0) return;

        if (_columnsStale || width != _columnsWidth) BuildColumns(width);
        if (Solid && hover == SeekHover.Lift) hover = SeekHover.Magnifier;
        if (hover != _hover)
        {
            _hover = hover;
            _ripples.Clear();
            _pointed = _swollen = false;
            foreach (Column column in _columns)
            {
                column.Raised = false;
                TuneRise(column);
            }
        }

        _time += dt;
        bool pointed = pointer.HasValue;
        double x = Math.Clamp(pointer?.X ?? _lastX, 0, width);
        if (pointed != _pointed)
        {
            _pointed = pointed;
            _ripples.Add(new Ripple(_time, x, pointed));
        }
        if (pointed) _lastX = x;
        _pointedColumn = pointed ? ColumnAt(x) : -1;

        if (!Solid && _columns.Count > 1)
        {
            int fillColumn = ColumnAt(Math.Clamp(_fill, 0, width));
            if (scrubbing && _lastFillColumn >= 0 && fillColumn != _lastFillColumn)
            {
                int step = fillColumn > _lastFillColumn ? 1 : -1;
                for (int i = _lastFillColumn + step; ; i += step)
                {
                    _columns[i].Detent.Velocity += DetentImpulse;
                    if (i == fillColumn) break;
                }
            }
            _lastFillColumn = scrubbing ? fillColumn : -1;
        }
        else
        {
            _lastFillColumn = -1;
        }

        foreach (Column column in _columns)
        {
            column.Rise.Target = RiseFor(column, x);
            column.Rise.Advance(dt);
            column.Detent.Advance(dt);
        }
        ForgetPassedRipples(width);

        _scrubBlend.Target = scrubbing ? 1 : 0;
        _scrubBlend.Advance(dt);

        double activityTarget = playing || scrubbing ? 1 : GlowPausedAlpha;
        _glowActivity += (activityTarget - _glowActivity) * (1 - Math.Exp(-dt / (playing || scrubbing ? GlowWakeSeconds : GlowSleepSeconds)));
        double bassTarget = playing ? Math.Clamp(bass, 0, 1) : 0;
        _glowBass += (bassTarget - _glowBass) * (1 - Math.Exp(-dt / (bassTarget > _glowBass ? GlowBassAttack : GlowBassRelease)));
        _glowSpeed.Target = Math.Clamp(fillVelocity / GlowSpeedScale, -1, 1);
        _glowSpeed.Advance(dt);

        if (scrubbing && pointer is { } at)
        {
            double dy = at.Y - ActualHeight / 2;
            _bowY.Target = Math.Sign(dy) * MaxBow * (1 - Math.Exp(-Math.Abs(dy) / BowResist));
            if (Math.Abs(_bowY.Value) < 0.05) _bowX.Snap(x);
            _bowX.Target = x;
        }
        else
        {
            _bowY.Target = 0;
        }
        _bowY.Advance(dt);
        _bowX.Advance(dt);

        bool lifting = _hover == SeekHover.Lift && _pointedColumn >= 0;
        string? text = lifting ? _columns[_pointedColumn].Text : null;
        if (text != null)
        {
            if (_tipShown.Value < 0.01) _tipX.Snap(_columns[_pointedColumn].Center);
            _tipX.Target = _columns[_pointedColumn].Center;
            _tipText = text;
        }
        _focus.Target = lifting ? 1 : 0;
        _tipShown.Target = text != null ? 1 : 0;
        _focus.Advance(dt);
        _tipShown.Advance(dt);
        _tipX.Advance(dt);
        InvalidateVisual();
    }

    void BuildColumns(double width)
    {
        _columns.Clear();
        _columnsWidth = width;
        _columnsStale = false;
        _lastFillColumn = -1;
        if (Solid)
        {
            int count = Math.Max(1, (int)Math.Ceiling(width / SampleStep));
            for (int i = 0; i < count; i++) _columns.Add(new Column(width * i / count, width * (i + 1) / count, null));
        }
        else
        {
            List<double> edges = Edges(width);
            for (int i = 0; i < edges.Count - 1; i++)
                _columns.Add(new Column(edges[i], edges[i + 1], TextWithin(edges[i], edges[i + 1], width)));
        }
        foreach (Column column in _columns)
        {
            column.Raised = _swollen;
            TuneRise(column);
        }
    }

    void ForgetPassedRipples(double width)
    {
        while (_ripples.Count > 0 && _time - _ripples[0].At >= width / WaveSpeed)
        {
            _swollen = _ripples[0].Swelling;
            _ripples.RemoveAt(0);
        }
    }

    void TuneRise(Column column)
    {
        if (_hover == SeekHover.Wave) column.Rise.Tune(520, 20);
        else column.Rise.Tune(420, 26);
    }

    string? TextWithin(double left, double right, double width)
    {
        for (int i = 0; i < Math.Min(_starts.Length, _texts.Length); i++)
        {
            double x = _starts[i] * width;
            if (x >= left && x < right && !Lyric.IsWordless(_texts[i])) return _texts[i].Trim();
        }
        return null;
    }

    int ColumnAt(double x)
    {
        for (int i = 0; i < _columns.Count; i++)
            if (x < _columns[i].Right) return i;
        return _columns.Count - 1;
    }

    double RiseFor(Column column, double x) => _hover switch
    {
        SeekHover.Lift => _pointedColumn >= 0 && _columns[_pointedColumn] == column ? LiftThickness : RestThickness,
        SeekHover.Wave => Raise(column) ? WaveThickness : RestThickness,
        _ => _pointed ? RestThickness + BulgeThickness * Math.Exp(-Math.Pow(DistanceTo(column, x) / BulgeReach, 2)) : RestThickness,
    };

    double DistanceTo(Column column, double x) =>
        Solid ? Math.Abs(column.Center - x) : Math.Max(0, Math.Max(column.Left - x, x - column.Right));

    bool Raise(Column column)
    {
        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            if (_time - _ripples[i].At < Math.Abs(column.Center - _ripples[i].From) / WaveSpeed) continue;
            column.Raised = _ripples[i].Swelling;
            break;
        }
        return column.Raised;
    }

    double ThicknessOf(Column column)
    {
        double hover = Math.Max(_thickness, column.Rise.Value);
        double blend = Math.Clamp(_scrubBlend.Value, 0, 1);
        return Math.Max(MinThickness, hover + (_thickness - hover) * blend + column.Detent.Value);
    }

    double BowAt(double x, double width)
    {
        if (Math.Abs(_bowY.Value) < 0.02 || width <= 0) return 0;
        double t = Math.Clamp(x / width, 0, 1);
        double ends = Math.Sin(Math.PI * t);
        double focus = Math.Exp(-Math.Pow((x - _bowX.Value) / (width * 0.38), 2));
        return _bowY.Value * ends * (0.45 + 0.55 * focus);
    }

    List<double> Edges(double width)
    {
        var edges = new List<double> { 0 };
        foreach (double start in _starts)
        {
            double x = start * width;
            if (x - edges[^1] >= MinSegmentWidth && width - x >= MinSegmentWidth) edges.Add(x);
        }
        edges.Add(width);
        return edges;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        if (_columnsStale || width != _columnsWidth) BuildColumns(width);

        double middle = height / 2, fill = Math.Clamp(_fill, 0, width);
        if (Solid) RenderSolid(dc, width, middle, fill);
        else RenderLines(dc, middle, fill);
    }

    void RenderSolid(DrawingContext dc, double width, double middle, double fill)
    {
        if (fill > 2) DrawGlow(dc, width, fill, middle, ThicknessOf(_columns[ColumnAt(fill)]), Colors.White, 1);

        Geometry shape = SolidShape(width, middle);
        dc.DrawGeometry(Shades.White(UnplayedOpacity), null, shape);
        if (fill <= 0) return;

        double radius = Math.Min(ThicknessOf(_columns[ColumnAt(fill)]) / 2, width / 2);
        double capX = fill - radius;
        double capY = middle + BowAt(Math.Max(0, capX), width);
        var reach = new CombinedGeometry(GeometryCombineMode.Union,
            new RectangleGeometry(new Rect(0, -BowClipPad, Math.Max(0, capX), ActualHeight + BowClipPad * 2)),
            new EllipseGeometry(new Point(capX, capY), radius, radius));
        Geometry filled = Geometry.Combine(shape, reach, GeometryCombineMode.Intersect, null, CapTolerance, ToleranceType.Absolute);
        filled.Freeze();
        dc.DrawGeometry(Brushes.White, null, filled);
    }

    Geometry SolidShape(double width, double middle)
    {
        double left = Math.Min(ThicknessOf(_columns[0]) / 2, width / 2);
        double right = Math.Min(ThicknessOf(_columns[^1]) / 2, width / 2);
        double leftY = middle + BowAt(left, width);
        double rightY = middle + BowAt(width - right, width);
        var shape = new StreamGeometry();
        using (StreamGeometryContext outline = shape.Open())
        {
            outline.BeginFigure(new Point(left, leftY - left), true, true);
            foreach (Column column in _columns)
                if (column.Center > left && column.Center < width - right)
                    outline.LineTo(new Point(column.Center, middle + BowAt(column.Center, width) - ThicknessOf(column) / 2), true, true);
            outline.LineTo(new Point(width - right, rightY - right), true, true);
            outline.ArcTo(new Point(width - right, rightY + right), new Size(right, right), 0, false, SweepDirection.Clockwise, true, true);
            for (int i = _columns.Count - 1; i >= 0; i--)
                if (_columns[i].Center > left && _columns[i].Center < width - right)
                    outline.LineTo(new Point(_columns[i].Center, middle + BowAt(_columns[i].Center, width) + ThicknessOf(_columns[i]) / 2), true, true);
            outline.LineTo(new Point(left, leftY + left), true, true);
            outline.ArcTo(new Point(left, leftY - left), new Size(left, left), 0, false, SweepDirection.Clockwise, true, true);
        }
        shape.Freeze();
        return shape;
    }

    void RenderLines(DrawingContext dc, double middle, double fill)
    {
        double width = ActualWidth;
        int last = _columns.Count - 1;
        int current = 0;
        while (current < last && _columns[current + 1].Left <= fill) current++;

        double focus = Math.Clamp(_focus.Value, 0, 1);
        Color accent = Accent.Color;
        Column active = _columns[current];
        double activeRight = active.Right - (current < last ? MarkGap / 2 : 0);
        double activeDim = current == _pointedColumn ? 1 : 1 - DimmedShare * focus;
        double tip = Math.Min(fill, activeRight);
        if (tip > 2) DrawGlow(dc, width, tip, middle, ThicknessOf(active), accent, activeDim);

        for (int i = 0; i <= last; i++)
        {
            Column column = _columns[i];
            double left = column.Left + (i > 0 ? MarkGap / 2 : 0), right = column.Right - (i < last ? MarkGap / 2 : 0);
            if (right <= left) continue;

            double thick = ThicknessOf(column);
            double segMid = middle + BowAt(column.Center, width);
            var segment = new Rect(left, segMid - thick / 2, right - left, thick);
            double radius = Math.Min(thick, segment.Width) / 2;
            bool pointed = i == _pointedColumn;
            double dim = pointed ? 1 : 1 - DimmedShare * focus;

            if (i < current)
            {
                dc.DrawRoundedRectangle(Shades.White(PassedOpacity * dim), null, segment, radius, radius);
                continue;
            }
            if (i > current)
            {
                double ahead = pointed ? UnplayedOpacity + (LitAheadOpacity - UnplayedOpacity) * focus : UnplayedOpacity;
                dc.DrawRoundedRectangle(Shades.White(ahead), null, segment, radius, radius);
                continue;
            }

            dc.DrawRoundedRectangle(_shades.Of(accent, AheadOpacity * dim), null, segment, radius, radius);
            if (fill <= left) continue;
            double reach = Math.Min(fill, right);
            var segClip = new RectangleGeometry(segment, radius, radius);
            var fillCap = new CombinedGeometry(GeometryCombineMode.Union,
                new RectangleGeometry(new Rect(0, -BowClipPad, Math.Max(0, reach - radius), ActualHeight + BowClipPad * 2)),
                new EllipseGeometry(new Point(reach - radius, segMid), radius, radius));
            Geometry filled = Geometry.Combine(segClip, fillCap, GeometryCombineMode.Intersect, null, CapTolerance, ToleranceType.Absolute);
            filled.Freeze();
            dc.DrawGeometry(_shades.Of(accent, dim), null, filled);
        }

        RenderTip(dc, middle);
    }

    void DrawGlow(DrawingContext dc, double width, double tip, double middle, double thick, Color color, double intensity)
    {
        double speed = Math.Clamp(_glowSpeed.Value, -1, 1);
        double fwd = Math.Max(0, speed), rev = Math.Max(0, -speed);
        double anchor = tip - thick * 0.35 - fwd * 2.5;
        if (!_glow || anchor <= 2) return;

        double maxTail = GlowMaxTail * (1 + GlowFwdTailBoost * fwd - GlowRevTailShrink * rev + GlowBassTail * _glowBass);
        double tail = Math.Min(maxTail, anchor);
        double intro = Math.Clamp(tail / GlowFadeIn, 0, 1);
        double motionAlpha = 1 + GlowFwdAlphaBoost * fwd - GlowRevAlphaDrop * rev + GlowBassAlpha * _glowBass;
        double alpha = Math.Clamp(intensity * intro * _glowActivity * motionAlpha, 0, 1);
        if (alpha <= 0.005) return;

        double tailShare = Math.Clamp(tail / Math.Max(maxTail, 1), 0, 1);
        double spreadScale = (1 - GlowFwdNarrow * fwd) * (1 + GlowBassSpread * _glowBass);
        double halfHeight = Math.Max(thick * 1.4, (GlowHalfHeight + Math.Max(0, thick - RestThickness) * 0.9) * spreadScale * (0.18 + 0.82 * tailShare));
        double anchorY = middle + BowAt(anchor, width);
        var rect = new Rect(anchor - tail, anchorY - halfHeight, tail / GlowTipOffset, halfHeight * 2);

        double slopeY = BowAt(anchor, width) - BowAt(Math.Max(0, anchor - 28), width);
        bool tilted = Math.Abs(slopeY) > 0.05;
        if (tilted) dc.PushTransform(new RotateTransform(Math.Atan2(slopeY, Math.Min(28, anchor)) * (180 / Math.PI), anchor, anchorY));

        if (color == Colors.White)
        {
            dc.PushOpacity(alpha);
            dc.DrawImage(GlowSprite, rect);
            dc.Pop();
        }
        else
        {
            _glowMask.Viewport = rect;
            dc.PushOpacityMask(_glowMask);
            dc.DrawRectangle(_shades.Of(color, alpha), null, rect);
            dc.Pop();
        }

        if (tilted) dc.Pop();
    }

    static BitmapSource CreateGlowSprite()
    {
        const int width = GlowSpriteWidth, height = GlowSpriteHeight, stride = width * 4;
        var pixels = new byte[height * stride];

        for (int y = 0; y < height; y++)
        {
            double v = Math.Abs((y + 0.5) / height - 0.5) * 2;
            double edgeV = Math.Clamp(1 - v, 0, 1);
            edgeV = edgeV * edgeV * (3 - 2 * edgeV);
            double frontShift = 0.055 * v + 0.16 * v * v;
            int row = y * stride;

            for (int x = 0; x < width; x++)
            {
                double u = (GlowTipOffset - (x + 0.5) / width) / GlowTipOffset;
                double uEff = u - frontShift;
                if (uEff <= -0.03) continue;

                double ramp = Math.Clamp((uEff + 0.03) / 0.1, 0, 1);
                ramp = ramp * ramp * (3 - 2 * ramp);
                double uPos = Math.Max(uEff, 0);
                double tail = Math.Clamp(1 - uPos, 0, 1);

                double coneSigma = 0.06 + 0.56 * Math.Pow(uPos, 0.85);
                double coneVert = Math.Exp(-0.5 * Math.Pow(v / coneSigma, 2));
                double cone = 0.64 * coneVert * (0.2 + 0.8 * (1 - Math.Exp(-uPos / 0.06))) * Math.Pow(tail, 1.75) * Math.Exp(-0.75 * uPos);

                double coreSigma = 0.048 + 0.19 * Math.Pow(uPos, 0.9);
                double coreVert = Math.Exp(-0.5 * Math.Pow(v / coreSigma, 2));
                double core = 0.72 * coreVert * Math.Pow(tail, 2.3) * Math.Exp(-2.1 * uPos);

                double raw = (cone + core) * ramp * edgeV;
                double alpha = Math.Clamp(1 - Math.Exp(-raw * 1.15), 0, 1);
                byte level = (byte)Math.Round(alpha * 255);
                int at = row + x * 4;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = pixels[at + 3] = level;
            }
        }

        var sprite = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        sprite.Freeze();
        return sprite;
    }

    void RenderTip(DrawingContext dc, double middle)
    {
        double shown = Math.Clamp(_tipShown.Value, 0, 1);
        if (shown < 0.01 || _tipText.Length == 0) return;

        double width = ActualWidth;
        var typeface = new Typeface((FontFamily)GetValue(TextElement.FontFamilyProperty), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        var text = new FormattedText(_tipText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface,
            TipFontSize, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, width - TipPadding * 2),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        double tipWidth = Math.Min(width, text.WidthIncludingTrailingWhitespace + TipPadding * 2);
        double center = Math.Clamp(_tipX.Value, tipWidth / 2, width - tipWidth / 2);
        double bottom = middle - LiftThickness / 2 - TipLift + (1 - shown) * TipRise;
        var pill = new Rect(center - tipWidth / 2, bottom - TipHeight, tipWidth, TipHeight);

        dc.PushOpacity(shown);
        dc.DrawRoundedRectangle(TipBack, null, pill, TipHeight / 2, TipHeight / 2);
        dc.DrawText(text, new Point(pill.X + TipPadding, pill.Y + (TipHeight - text.Height) / 2));
        dc.Pop();
    }
}
