using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

public sealed class Goo : FrameworkElement
{
    const double RimWidth = 1;
    const double MinBevelInset = 2.0, MaxBevelInset = 3.8, BevelShare = 0.075;
    const double TearGap = 7.5;
    const double NeckGrip = 0.5;
    const double NeckHandle = 2.4;
    const double Tolerance = 0.02;
    const double HazeOpacity = 0.14;
    const byte GlassAlpha = 0x48;
    const double SpecularReach = 74;
    const double SpecularRimOpacity = 0.48;

    static readonly Color PlainTop = Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF);
    static readonly Color PlainMid = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF);
    static readonly Color PlainBottom = Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF);
    static readonly Color GlassTop = Color.FromArgb(0x64, 0xFF, 0xFF, 0xFF);
    static readonly Color GlassMid = Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF);
    static readonly Color GlassBottom = Color.FromArgb(0x34, 0xFF, 0xFF, 0xFF);
    static readonly double[] HazeReach = [2, 3, 4];
    static readonly (double At, double Level)[] FlashFrames = [(0, 0), (0.04, 1), (0.13, 0.3), (0.19, 0.9), (0.5, 0), (1, 0)];

    readonly LinearGradientBrush _rim = CreateRimBrush();
    readonly RadialGradientBrush _specularBrush = CreateSpecularBrush();
    readonly SolidColorBrush _fill = new(Colors.Black);
    readonly Light _flash = new();
    readonly Pen _edge, _specularEdge;

    Rect _pill = Rect.Empty, _bubble = Rect.Empty;
    double _radius;
    bool _glass;
    Color[]? _palette;
    Point? _pointer;
    Geometry? _innerGeometry, _outerGeometry, _bevelRingGeometry;
    Geometry[] _innerParts = [];

    public Goo()
    {
        _edge = new Pen(_rim, 2 * RimWidth) { LineJoin = PenLineJoin.Round };
        _specularEdge = new Pen(_specularBrush, 2 * RimWidth) { LineJoin = PenLineJoin.Round };
    }

    public Geometry? InnerGeometry => _innerGeometry;

    public Geometry? OuterGeometry => _outerGeometry;

    public Geometry? BevelRingGeometry => _bevelRingGeometry;

    public (Rect Box, double Radius) PillInside => Inside(_pill, _radius);

    public (Rect Box, double Radius) BubbleInside => Inside(_bubble, _bubble.Height / 2);

    public void Tint(Color? color, Duration time) =>
        Tint(color is { } c ? [c] : null, time);

    public void Tint(IReadOnlyList<Color>? palette, Duration time)
    {
        _palette = palette is { Count: > 0 } ? [.. palette] : null;
        ApplyRimColors(time);
    }

    public void SetPointer(Point? at)
    {
        if (_pointer == at) return;
        _pointer = at;
        InvalidateVisual();
    }

    public void Thin(bool glass, Duration time, Action thinned)
    {
        _glass = glass;
        var fade = new ColorAnimation(Color.FromArgb(glass ? GlassAlpha : byte.MaxValue, 0, 0, 0), time);
        fade.Completed += (_, _) => thinned();
        _fill.BeginAnimation(SolidColorBrush.ColorProperty, fade);
        ApplyRimColors(time);
    }

    public void StartFlashing(Color color, TimeSpan round)
    {
        _flash.Edge.Color = _flash.Mist.Color = color;
        _flash.Edge.BeginAnimation(Brush.OpacityProperty, Flashes(1));
        _flash.Mist.BeginAnimation(Brush.OpacityProperty, Flashes(HazeOpacity));

        DoubleAnimationUsingKeyFrames Flashes(double share)
        {
            var flashes = new DoubleAnimationUsingKeyFrames { Duration = round, RepeatBehavior = RepeatBehavior.Forever, FillBehavior = FillBehavior.Stop };
            foreach ((double at, double level) in FlashFrames)
                flashes.KeyFrames.Add(new LinearDoubleKeyFrame(level * share, KeyTime.FromPercent(at)));
            return flashes;
        }
    }

    public void StopFlashing()
    {
        _flash.Edge.BeginAnimation(Brush.OpacityProperty, null);
        _flash.Mist.BeginAnimation(Brush.OpacityProperty, null);
    }

    public void SetShape(Rect pill, double radius, Rect bubble)
    {
        if (pill == _pill && radius == _radius && bubble == _bubble) return;
        _pill = pill;
        _radius = radius;
        _bubble = bubble;
        RebuildGeometry();
        InvalidateVisual();
    }

    void RebuildGeometry()
    {
        if (_pill.IsEmpty)
        {
            _innerGeometry = _outerGeometry = _bevelRingGeometry = null;
            _innerParts = [];
            return;
        }

        double bevel = Math.Clamp(Math.Min(_pill.Width, _pill.Height) * BevelShare, MinBevelInset, MaxBevelInset);
        Geometry innerPill = Outline(_pill, _radius);
        Geometry outerPill = Squircle.Of(_pill, _radius);
        Geometry corePill = InsetOutline(_pill, _radius, RimWidth + bevel);
        if (_bubble.IsEmpty)
        {
            _innerGeometry = innerPill;
            _outerGeometry = outerPill;
            _bevelRingGeometry = Ring(innerPill, corePill);
            _innerParts = [innerPill];
            return;
        }

        double br = _bubble.Height / 2;
        double bubbleBevel = Math.Clamp(_bubble.Height * BevelShare, MinBevelInset, MaxBevelInset);
        Geometry innerBubble = Outline(_bubble, br);
        Geometry outerBubble = Squircle.Of(_bubble, br);
        Geometry coreBubble = InsetOutline(_bubble, br, RimWidth + bubbleBevel);
        Geometry? innerNeck = CreateNeck(RimWidth);
        if (innerNeck == null && !_pill.IntersectsWith(_bubble))
        {
            var innerGroup = new GeometryGroup { FillRule = FillRule.Nonzero };
            innerGroup.Children.Add(innerPill);
            innerGroup.Children.Add(innerBubble);
            innerGroup.Freeze();

            var outerGroup = new GeometryGroup { FillRule = FillRule.Nonzero };
            outerGroup.Children.Add(outerPill);
            outerGroup.Children.Add(outerBubble);
            outerGroup.Freeze();

            var ringGroup = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ringGroup.Children.Add(innerPill);
            ringGroup.Children.Add(corePill);
            ringGroup.Children.Add(innerBubble);
            ringGroup.Children.Add(coreBubble);
            ringGroup.Freeze();

            _innerGeometry = innerGroup;
            _outerGeometry = outerGroup;
            _bevelRingGeometry = ringGroup;
            _innerParts = [innerPill, innerBubble];
            return;
        }

        Geometry innerBody = Union(innerPill, innerBubble);
        if (innerNeck != null) innerBody = Union(innerBody, innerNeck);
        innerBody.Freeze();

        Geometry? outerNeck = CreateNeck(0);
        Geometry outerBody = Union(outerPill, outerBubble);
        if (outerNeck != null) outerBody = Union(outerBody, outerNeck);
        outerBody.Freeze();

        Geometry? coreNeck = CreateNeck(RimWidth + Math.Min(bevel, bubbleBevel));
        Geometry coreBody = Union(corePill, coreBubble);
        if (coreNeck != null) coreBody = Union(coreBody, coreNeck);
        coreBody.Freeze();

        _innerGeometry = innerBody;
        _outerGeometry = outerBody;
        _bevelRingGeometry = Ring(innerBody, coreBody);
        _innerParts = [innerBody];
    }

    static Geometry Ring(Geometry outer, Geometry inner)
    {
        var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
        ring.Children.Add(outer);
        ring.Children.Add(inner);
        ring.Freeze();
        return ring;
    }

    void ApplyRimColors(Duration time)
    {
        Color top, mid, mid2, bottom;

        if (_palette is { Length: > 0 } p)
        {
            Color c0 = p[0];
            Color c1 = p[Math.Min(1, p.Length - 1)];
            Color c2 = p[Math.Min(2, p.Length - 1)];

            top = Brighten(c0, 0.25).WithAlpha(_glass ? 0.68 : 0.62);
            mid = c1.WithAlpha(_glass ? 0.24 : 0.30);
            mid2 = c2.WithAlpha(_glass ? 0.28 : 0.36);
            bottom = Brighten(c0, 0.12).WithAlpha(_glass ? 0.48 : 0.46);
        }
        else
        {
            top = _glass ? GlassTop : PlainTop;
            mid = _glass ? GlassMid : PlainMid;
            mid2 = mid;
            bottom = _glass ? GlassBottom : PlainBottom;
        }

        AnimateStop(_rim, 0, top, time);
        AnimateStop(_rim, 1, mid, time);
        AnimateStop(_rim, 2, mid2, time);
        AnimateStop(_rim, 3, bottom, time);
    }

    static void AnimateStop(LinearGradientBrush brush, int index, Color to, Duration time) =>
        brush.GradientStops[index].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(to, time));

    protected override void OnRender(DrawingContext dc)
    {
        if (_innerParts.Length == 0) return;
        foreach (Geometry part in _innerParts) DrawBody(dc, part);
    }

    public static Geometry Union(Geometry a, Geometry b) =>
        Geometry.Combine(a, b, GeometryCombineMode.Union, null, Tolerance, ToleranceType.Absolute);

    void DrawBody(DrawingContext dc, Geometry body)
    {
        Rect bounds = body.Bounds;
        Rect around = bounds;
        around.Inflate(2 * RimWidth, 2 * RimWidth);
        var outside = new GeometryGroup { FillRule = FillRule.EvenOdd };
        outside.Children.Add(new RectangleGeometry(around));
        outside.Children.Add(body);

        dc.PushClip(outside);
        dc.DrawGeometry(null, _edge, body);
        if (_pointer is { } at && bounds.Height > 44)
        {
            _specularBrush.Center = _specularBrush.GradientOrigin = at;
            _specularBrush.RadiusX = _specularBrush.RadiusY = SpecularReach;
            dc.DrawGeometry(null, _specularEdge, body);
        }
        dc.DrawGeometry(null, _flash.Line, body);
        dc.Pop();

        dc.DrawGeometry(_fill, null, body);
        foreach (Pen mist in _flash.Mists) dc.DrawGeometry(null, mist, body);
    }

    sealed class Light
    {
        public readonly SolidColorBrush Edge = new(Colors.White) { Opacity = 0 }, Mist = new(Colors.White) { Opacity = 0 };
        public readonly Pen Line;
        public readonly Pen[] Mists;

        public Light()
        {
            Line = new Pen(Edge, 2 * RimWidth) { LineJoin = PenLineJoin.Round };
            Mists = HazeReach.Select(reach => new Pen(Mist, 2 * reach) { LineJoin = PenLineJoin.Round }).ToArray();
        }
    }

    static LinearGradientBrush CreateRimBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.12, 0), EndPoint = new Point(0.88, 1) };
        brush.GradientStops.Add(new GradientStop(PlainTop, 0));
        brush.GradientStops.Add(new GradientStop(PlainMid, 0.32));
        brush.GradientStops.Add(new GradientStop(PlainMid, 0.72));
        brush.GradientStops.Add(new GradientStop(PlainBottom, 1));
        return brush;
    }

    static RadialGradientBrush CreateSpecularBrush() =>
        new(Colors.White.WithAlpha(SpecularRimOpacity), Colors.Transparent) { MappingMode = BrushMappingMode.Absolute };

    static Color Brighten(Color c, double share) => Color.FromRgb(
        (byte)Math.Round(c.R + (255 - c.R) * share),
        (byte)Math.Round(c.G + (255 - c.G) * share),
        (byte)Math.Round(c.B + (255 - c.B) * share));

    static Geometry Outline(Rect rect, double radius)
    {
        (Rect box, double r) = Inside(rect, radius);
        return Squircle.Of(box, r);
    }

    static Geometry InsetOutline(Rect rect, double radius, double inset)
    {
        if (rect.IsEmpty) return Geometry.Empty;
        double dx = Math.Min(inset, rect.Width / 2 - 0.5);
        double dy = Math.Min(inset, rect.Height / 2 - 0.5);
        if (dx <= 0 || dy <= 0) return Geometry.Empty;
        rect.Inflate(-dx, -dy);
        return Squircle.Of(rect, Math.Max(radius - inset, 0));
    }

    static (Rect Box, double Radius) Inside(Rect rect, double radius)
    {
        if (rect.IsEmpty) return (rect, 0);
        rect.Inflate(-Math.Min(RimWidth, rect.Width / 2), -Math.Min(RimWidth, rect.Height / 2));
        return (rect, Math.Max(radius - RimWidth, 0));
    }

    Geometry? CreateNeck(double inset)
    {
        var c1 = new Point(_pill.Right - _radius, _pill.Top + _radius);
        var c2 = new Point(_bubble.Left + _bubble.Height / 2, _bubble.Top + _bubble.Height / 2);
        return c2.X > c1.X ? Neck(c1, _radius - inset, c2, _bubble.Height / 2 - inset, TearGap) : null;
    }

    public static Geometry? Neck(Point c1, double r1, Point c2, double r2, double tearGap)
    {
        Vector between = c2 - c1;
        double d = between.Length, gap = d - r1 - r2;
        if (r1 <= 0 || r2 <= 0 || d <= Math.Abs(r1 - r2) || gap >= tearGap) return null;

        double u1 = 0, u2 = 0;
        if (gap < 0)
        {
            u1 = Math.Acos(Math.Clamp((r1 * r1 + d * d - r2 * r2) / (2 * r1 * d), -1, 1));
            u2 = Math.Acos(Math.Clamp((r2 * r2 + d * d - r1 * r1) / (2 * r2 * d), -1, 1));
        }

        double grip = NeckGrip * (1 - Math.Clamp(gap / tearGap, 0, 1));
        double axis = Math.Atan2(between.Y, between.X), wide = Math.Acos((r1 - r2) / d);
        double a1 = axis + u1 + (wide - u1) * grip, a2 = axis - u1 - (wide - u1) * grip;
        double a3 = axis + Math.PI - u2 - (Math.PI - u2 - wide) * grip, a4 = axis - Math.PI + u2 + (Math.PI - u2 - wide) * grip;
        Point p1 = PointOnCircle(c1, a1, r1), p2 = PointOnCircle(c1, a2, r1), p3 = PointOnCircle(c2, a3, r2), p4 = PointOnCircle(c2, a4, r2);

        double reach = Math.Min(grip * NeckHandle, (p1 - p3).Length / (r1 + r2)) * Math.Min(1, 2 * d / (r1 + r2));
        const double Quarter = Math.PI / 2;
        var neck = new StreamGeometry();
        using (StreamGeometryContext g = neck.Open())
        {
            g.BeginFigure(p1, true, true);
            g.BezierTo(PointOnCircle(p1, a1 - Quarter, r1 * reach), PointOnCircle(p3, a3 + Quarter, r2 * reach), p3, true, true);
            g.LineTo(p4, true, true);
            g.BezierTo(PointOnCircle(p4, a4 - Quarter, r2 * reach), PointOnCircle(p2, a2 + Quarter, r1 * reach), p2, true, true);
        }
        return neck;
    }

    static Point PointOnCircle(Point center, double angle, double radius) =>
        new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
}
