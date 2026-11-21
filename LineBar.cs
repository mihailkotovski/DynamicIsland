using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

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

    static readonly Brush TipBack = Shades.Frozen(new SolidColorBrush(Color.FromArgb(0xF0, 0x24, 0x24, 0x24)));

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
        public bool Raised { get; set; }
    }

    readonly record struct Ripple(double At, double From, bool Swelling);

    readonly Shades _shades = new();
    readonly RadialGradientBrush _bloom = new(Colors.White, Colors.Transparent);
    readonly List<Column> _columns = [];
    readonly List<Ripple> _ripples = [];
    readonly Spring _focus = new(0, 300, 30), _tipShown = new(0, 300, 30), _tipX = new(0, 420, 32);

    double[] _starts = [];
    string[] _texts = [];
    double _fill, _thickness = RestThickness;
    double _columnsWidth = -1;
    bool _columnsStale = true;
    SeekHover _hover = SeekHover.Magnifier;
    double _time, _lastX;
    bool _pointed, _swollen;
    int _pointedColumn = -1;
    string _tipText = "";

    public SolidColorBrush Accent
    {
        get => (SolidColorBrush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public bool Solid { get; set; }

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

    internal void Advance(double dt, double? pointer, SeekHover hover)
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
        double x = Math.Clamp(pointer ?? _lastX, 0, width);
        if (pointed != _pointed)
        {
            _pointed = pointed;
            _ripples.Add(new Ripple(_time, x, pointed));
        }
        if (pointed) _lastX = x;
        _pointedColumn = pointed ? ColumnAt(x) : -1;

        foreach (Column column in _columns)
        {
            column.Rise.Target = RiseFor(column, x);
            column.Rise.Advance(dt);
        }
        ForgetPassedRipples(width);

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

    double ThicknessOf(Column column) => Math.Max(MinThickness, Math.Max(_thickness, column.Rise.Value));

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
        Geometry shape = SolidShape(width, middle);
        dc.DrawGeometry(Shades.White(UnplayedOpacity), null, shape);
        if (fill <= 0) return;

        double radius = Math.Min(ThicknessOf(_columns[ColumnAt(fill)]) / 2, width / 2);
        var reach = new CombinedGeometry(GeometryCombineMode.Union,
            new RectangleGeometry(new Rect(0, 0, Math.Max(0, fill - radius), ActualHeight)),
            new EllipseGeometry(new Point(fill - radius, middle), radius, radius));
        dc.PushClip(reach);
        dc.DrawGeometry(Brushes.White, null, shape);
        dc.Pop();

        if (_pointed && fill > 2)
        {
            double thick = ThicknessOf(_columns[ColumnAt(fill)]);
            _bloom.GradientStops[0].Color = Colors.White.WithAlpha(0.32);
            _bloom.GradientStops[1].Color = Colors.Transparent;
            dc.DrawEllipse(_bloom, null, new Point(fill - radius * 0.4, middle), thick * 1.1, thick * 1.1);
        }
    }

    Geometry SolidShape(double width, double middle)
    {
        double left = Math.Min(ThicknessOf(_columns[0]) / 2, width / 2);
        double right = Math.Min(ThicknessOf(_columns[^1]) / 2, width / 2);
        var shape = new StreamGeometry();
        using (StreamGeometryContext outline = shape.Open())
        {
            outline.BeginFigure(new Point(left, middle - left), true, true);
            foreach (Column column in _columns)
                if (column.Center > left && column.Center < width - right)
                    outline.LineTo(new Point(column.Center, middle - ThicknessOf(column) / 2), true, true);
            outline.LineTo(new Point(width - right, middle - right), true, true);
            outline.ArcTo(new Point(width - right, middle + right), new Size(right, right), 0, false, SweepDirection.Clockwise, true, true);
            for (int i = _columns.Count - 1; i >= 0; i--)
                if (_columns[i].Center > left && _columns[i].Center < width - right)
                    outline.LineTo(new Point(_columns[i].Center, middle + ThicknessOf(_columns[i]) / 2), true, true);
            outline.LineTo(new Point(left, middle + left), true, true);
            outline.ArcTo(new Point(left, middle - left), new Size(left, left), 0, false, SweepDirection.Clockwise, true, true);
        }
        shape.Freeze();
        return shape;
    }

    void RenderLines(DrawingContext dc, double middle, double fill)
    {
        int last = _columns.Count - 1;
        int current = 0;
        while (current < last && _columns[current + 1].Left <= fill) current++;

        double focus = Math.Clamp(_focus.Value, 0, 1);
        Color accent = Accent.Color;
        for (int i = 0; i <= last; i++)
        {
            Column column = _columns[i];
            double left = column.Left + (i > 0 ? MarkGap / 2 : 0), right = column.Right - (i < last ? MarkGap / 2 : 0);
            if (right <= left) continue;

            double thick = ThicknessOf(column);
            var segment = new Rect(left, middle - thick / 2, right - left, thick);
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
                new RectangleGeometry(new Rect(0, 0, Math.Max(0, reach - radius), ActualHeight)),
                new EllipseGeometry(new Point(reach - radius, middle), radius, radius));
            dc.PushClip(segClip);
            dc.PushClip(fillCap);
            dc.DrawRoundedRectangle(_shades.Of(accent, dim), null, segment, radius, radius);
            dc.Pop();
            dc.Pop();

            if (reach > left + 1)
            {
                double glowRadius = thick * 1.15;
                _bloom.GradientStops[0].Color = accent.WithAlpha(0.38 * dim);
                _bloom.GradientStops[1].Color = accent.WithAlpha(0);
                dc.DrawEllipse(_bloom, null, new Point(reach - radius * 0.4, middle), glowRadius, glowRadius);
            }
        }

        RenderTip(dc, middle);
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
