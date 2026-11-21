using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

public enum Glyph
{
    Mute, Quiet, Mid, Loud, Headphones, Speaker, Vpn, Offline, Wifi, Wired, Bell, Note, Battery, Minus, Plus, Chevron, Back,
    Timer, Gear, Lines, Quote, Sparkle, Rim, Expand, Windows, Power, Look, Size, Gap, Drop, Moon, Tray, Cross, Bolt, VpnOff, Bars, Update,
    Segments, Stars, Pointer, Record, Folder, Bulge, Sun, Cloud, CloudSun, CloudMoon, Rain, Snow, Storm, Fog, Qr, Pin, Location, Search,
    Check, PointerText, Parting,
}

public sealed class Icon : FrameworkElement
{
    const double GridSize = 24;
    const double SymbolEm = 20;
    const double WidestSymbol = GridSize;
    const double Tolerance = 0.01;
    const double PieceSeconds = 0.16;
    const double PieceStagger = 0.06;
    const double Absent = 0.001, Complete = 0.999;
    const double SweepReach = GridSize * 4;
    const string SymbolFont = "./Fonts/#SF Pro Text";
    static readonly FontWeight SymbolWeight = FontWeights.Medium;
    static readonly Vector Slant = new(1, 1), Across = new(1, 0);

    readonly record struct Symbol(string Name, int Code);

    sealed record Piece(Geometry Shape, Geometry Cut, Vector Sweep)
    {
        public bool Drawn => Sweep != default;
    }

    struct Stage
    {
        public double Shown, From, To, Start;
    }

    static readonly GlyphTypeface? Face = LoadFace();

    static readonly Symbol SpeakerWaves = new("speaker.wave.3.fill", 0x1002A9);
    static readonly Symbol SpeakerSlash = new("speaker.slash.fill", 0x1002A3);
    static readonly Symbol WifiArcs = new("wifi", 0x100647);
    static readonly Symbol WifiSlash = new("wifi.slash", 0x100648);
    static readonly Symbol Shield = new("shield.fill", 0x100667);
    static readonly Symbol CheckedShield = new("checkmark.shield.fill", 0x10079C);

    static readonly Piece[] SoundPieces = BuildSoundPieces();
    static readonly Piece[] NetworkPieces = BuildNetworkPieces();
    static readonly Piece[] ShieldPieces = BuildShieldPieces();

    static readonly Dictionary<Glyph, (Piece[] Pieces, int Mask)> Composite = new()
    {
        [Glyph.Mute] = (SoundPieces, 0b10001),
        [Glyph.Quiet] = (SoundPieces, 0b00011),
        [Glyph.Mid] = (SoundPieces, 0b00111),
        [Glyph.Loud] = (SoundPieces, 0b01111),
        [Glyph.Wifi] = (NetworkPieces, 0b0111),
        [Glyph.Offline] = (NetworkPieces, 0b1111),
        [Glyph.Vpn] = (ShieldPieces, 0b11),
        [Glyph.VpnOff] = (ShieldPieces, 0b01),
    };

    static readonly Dictionary<Glyph, Symbol> Symbols = new()
    {
        [Glyph.Headphones] = new("headphones", 0x100448),
        [Glyph.Speaker] = new("hifispeaker.fill", 0x10074F),
        [Glyph.Wired] = new("cable.connector", 0x100EA6),
        [Glyph.Bell] = new("bell.fill", 0x1002DA),
        [Glyph.Note] = new("music.note", 0x10046A),
        [Glyph.Battery] = new("battery.100", 0x1006E8),
        [Glyph.Minus] = new("minus", 0x10017D),
        [Glyph.Plus] = new("plus", 0x10017C),
        [Glyph.Chevron] = new("chevron.right", 0x10018A),
        [Glyph.Back] = new("chevron.left", 0x100189),
        [Glyph.Timer] = new("timer", 0x100431),
        [Glyph.Gear] = new("gearshape.fill", 0x1008CC),
        [Glyph.Lines] = new("text.alignleft", 0x100300),
        [Glyph.Quote] = new("quote.bubble", 0x10032E),
        [Glyph.Sparkle] = new("sparkle", 0x100AF8),
        [Glyph.Rim] = new("capsule.inset.filled", 0x100F9A),
        [Glyph.Expand] = new("viewfinder", 0x1003B9),
        [Glyph.Windows] = new("square.grid.2x2.fill", 0x1006E7),
        [Glyph.Update] = new("arrow.down.to.line", 0x100140),
        [Glyph.Power] = new("power", 0x1001A8),
        [Glyph.Look] = new("circle.righthalf.filled", 0x100003),
        [Glyph.Size] = new("arrow.up.left.and.arrow.down.right", 0x10014A),
        [Glyph.Gap] = new("arrow.up.to.line", 0x10013F),
        [Glyph.Drop] = new("drop.fill", 0x100812),
        [Glyph.Moon] = new("moon.fill", 0x1001BA),
        [Glyph.Tray] = new("tray.fill", 0x100224),
        [Glyph.Cross] = new("xmark", 0x100184),
        [Glyph.Bolt] = new("bolt.fill", 0x1002E6),
        [Glyph.Bars] = new("waveform", 0x10066B),
        [Glyph.Segments] = new("rectangle.bottomthird.inset.filled", 0x100A28),
        [Glyph.Stars] = new("sparkles", 0x1001BF),
        [Glyph.Pointer] = new("cursorarrow", 0x100ACC),
        [Glyph.PointerText] = new("contextualmenu.and.cursorarrow", 0x100C63),
        [Glyph.Parting] = new("character.cursor.ibeam", 0x10016B),
        [Glyph.Bulge] = new("cursorarrow.motionlines", 0x1008E0),
        [Glyph.Record] = new("record.circle", 0x100899),
        [Glyph.Folder] = new("folder.fill", 0x100216),
        [Glyph.Sun] = new("sun.max.fill", 0x1001AE),
        [Glyph.Cloud] = new("cloud.fill", 0x1001C3),
        [Glyph.CloudSun] = new("cloud.sun.fill", 0x1001D5),
        [Glyph.CloudMoon] = new("cloud.moon.fill", 0x1001DB),
        [Glyph.Rain] = new("cloud.rain.fill", 0x1001C7),
        [Glyph.Snow] = new("cloud.snow.fill", 0x1001CF),
        [Glyph.Storm] = new("cloud.bolt.fill", 0x1001D3),
        [Glyph.Fog] = new("cloud.fog.fill", 0x1001CB),
        [Glyph.Qr] = new("qrcode", 0x100582),
        [Glyph.Pin] = new("mappin.and.ellipse", 0x1003AB),
        [Glyph.Location] = new("location.fill", 0x1002D2),
        [Glyph.Search] = new("magnifyingglass", 0x1002AB),
        [Glyph.Check] = new("checkmark", 0x100185),
    };

    static readonly Dictionary<Glyph, Geometry> Outlines = [];
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(Glyph), typeof(Icon),
        new FrameworkPropertyMetadata(Glyph.Note, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Icon)d).OnKindChanged()));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    readonly FrameLoop _loop;
    Piece[]? _pieces;
    Stage[] _stages = [];

    public Icon() => _loop = new FrameLoop(_ => Advance());

    public Glyph Kind
    {
        get => (Glyph)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public void Play(Glyph? from, double wait)
    {
        if (_pieces == null || !Composite.TryGetValue(Kind, out var composite)) return;
        int had = from is { } other && Composite.TryGetValue(other, out var before) && before.Pieces == composite.Pieces ? before.Mask : 0;
        for (int i = 0; i < _stages.Length; i++) _stages[i].Shown = Bit(had, i);
        Animate(composite.Mask, wait);
    }

    void OnKindChanged()
    {
        if (!Composite.TryGetValue(Kind, out var composite))
        {
            _pieces = null;
            _loop.Stop();
            return;
        }

        if (composite.Pieces == _pieces && IsVisible)
        {
            Animate(composite.Mask, 0);
            return;
        }
        _pieces = composite.Pieces;
        _stages = new Stage[_pieces.Length];
        for (int i = 0; i < _stages.Length; i++) _stages[i].Shown = Bit(composite.Mask, i);
        _loop.Stop();
    }

    void Animate(int mask, double wait)
    {
        double at = Clock.Elapsed.TotalSeconds + wait;
        for (int i = _stages.Length - 1; i >= 0; i--)
            if (Bit(mask, i) == 0) at = Schedule(ref _stages[i], 0, at);
        for (int i = 0; i < _stages.Length; i++)
            if (Bit(mask, i) != 0) at = Schedule(ref _stages[i], 1, at);
        InvalidateVisual();
        _loop.Start();
    }

    static double Schedule(ref Stage stage, double to, double at)
    {
        stage.From = stage.Shown;
        stage.To = to;
        stage.Start = at;
        return Math.Abs(to - stage.Shown) > Absent ? at + PieceStagger : at;
    }

    bool Advance()
    {
        double now = Clock.Elapsed.TotalSeconds;
        bool moving = false;
        for (int i = 0; i < _stages.Length; i++)
        {
            ref Stage stage = ref _stages[i];
            double t = Math.Clamp((now - stage.Start) / PieceSeconds, 0, 1);
            stage.Shown = stage.From + (stage.To - stage.From) * t * t * (3 - 2 * t);
            moving |= t < 1;
        }
        InvalidateVisual();
        return moving;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / GridSize, size / GridSize));
        if (_loop.Running && _pieces != null) DrawPieces(dc, _pieces);
        else dc.DrawGeometry(Fill, null, Outline(Kind));
        dc.Pop();
        dc.Pop();
    }

    void DrawPieces(DrawingContext dc, Piece[] pieces)
    {
        var parts = new (Geometry? Shape, double Opacity)[pieces.Length];
        Geometry? cuts = null;
        for (int i = pieces.Length - 1; i >= 0; i--)
        {
            Piece piece = pieces[i];
            double shown = _stages[i].Shown;
            if (shown <= Absent) continue;

            double share = piece.Drawn ? shown : 1;
            Geometry shape = Revealed(piece, piece.Shape, share);
            if (cuts != null && !shape.IsEmpty()) shape = Exclude(shape, cuts);
            parts[i] = (shape, piece.Drawn ? 1 : shown);
            if (piece.Cut.IsEmpty()) continue;
            Geometry cut = Revealed(piece, piece.Cut, share);
            cuts = cuts == null ? cut : Union(cuts, cut);
        }

        foreach ((Geometry? shape, double opacity) in parts)
        {
            if (shape == null || shape.IsEmpty()) continue;
            dc.PushOpacity(opacity);
            dc.DrawGeometry(Fill, null, shape);
            dc.Pop();
        }
    }

    static Geometry Revealed(Piece piece, Geometry part, double share)
    {
        if (share >= Complete || part.IsEmpty()) return part;

        Rect bounds = Rect.Union(piece.Shape.Bounds, piece.Cut.Bounds);
        Vector along = piece.Sweep / piece.Sweep.Length, across = new(-along.Y, along.X);
        Point[] corners = [bounds.TopLeft, bounds.TopRight, bounds.BottomLeft, bounds.BottomRight];
        double start = corners.Min(corner => (Vector)corner * along), end = corners.Max(corner => (Vector)corner * along);
        double reach = start + (end - start) * Math.Max(share, 0);

        var front = new StreamGeometry();
        using (StreamGeometryContext g = front.Open())
        {
            g.BeginFigure((Point)(along * (start - 1) - across * SweepReach), true, true);
            g.LineTo((Point)(along * reach - across * SweepReach), false, false);
            g.LineTo((Point)(along * reach + across * SweepReach), false, false);
            g.LineTo((Point)(along * (start - 1) + across * SweepReach), false, false);
        }
        return Intersect(part, front);
    }

    static int Bit(int mask, int index) => mask >> index & 1;

    static GlyphTypeface? LoadFace()
    {
        var family = new FontFamily(new Uri("pack://application:,,,/"), SymbolFont);
        var typeface = new Typeface(family, FontStyles.Normal, SymbolWeight, FontStretches.Normal);
        return typeface.TryGetGlyphTypeface(out GlyphTypeface face) ? face : null;
    }

    static Geometry Outline(Symbol symbol)
    {
        if (Face == null) return Geometry.Empty;
        if (Face.CharacterToGlyphMap.TryGetValue(symbol.Code, out ushort index)) return Face.GetGlyphOutline(index, SymbolEm, SymbolEm);

        App.Log(new KeyNotFoundException($"SF Symbol {symbol.Name} is missing from {SymbolFont}"));
        return Geometry.Empty;
    }

    static Geometry Outline(Glyph kind)
    {
        if (Outlines.TryGetValue(kind, out Geometry? outline)) return outline;
        outline = Composite.TryGetValue(kind, out var composite) ? Outline(composite.Pieces, composite.Mask) : Placed(Outline(Symbols[kind]));
        return Outlines[kind] = outline;
    }

    static Geometry Outline(Piece[] pieces, int mask)
    {
        Geometry shape = Geometry.Empty;
        for (int i = 0; i < pieces.Length; i++)
        {
            if (Bit(mask, i) == 0) continue;
            if (!pieces[i].Cut.IsEmpty()) shape = Exclude(shape, pieces[i].Cut);
            shape = Union(shape, pieces[i].Shape);
        }
        shape.Freeze();
        return shape;
    }

    static Piece[] BuildSoundPieces()
    {
        Geometry[] parts = Contours(Outline(SpeakerWaves), 4, bounds => bounds.Left);
        return Placed([Fading(parts[0]), Fading(parts[1]), Fading(parts[2]), Fading(parts[3]), Drawn(Outline(SpeakerSlash), parts[0], Slant)]);
    }

    static Piece[] BuildNetworkPieces()
    {
        Geometry arcs = Outline(WifiArcs);
        Geometry[] parts = Contours(arcs, 3, bounds => -bounds.Bottom);
        return Placed([Fading(parts[0]), Fading(parts[1]), Fading(parts[2]), Drawn(Outline(WifiSlash), arcs, Slant)]);
    }

    static Piece[] BuildShieldPieces()
    {
        Geometry shield = Outline(Shield);
        return Placed([Fading(shield), Drawn(Outline(CheckedShield), shield, Across)]);
    }

    static Piece Fading(Geometry shape) => new(shape, Geometry.Empty, default);

    static Piece Drawn(Geometry whole, Geometry under, Vector sweep) => new(Exclude(whole, under), Exclude(under, whole), sweep);

    static Geometry[] Contours(Geometry outline, int count, Func<Rect, double> order)
    {
        PathGeometry path = PathGeometry.CreateFromGeometry(outline);
        Geometry[] contours = path.Figures
            .Select(figure => (Geometry)new PathGeometry([figure.Clone()], path.FillRule, path.Transform))
            .OrderBy(contour => order(contour.Bounds))
            .ToArray();
        if (contours.Length == count) return contours;

        var whole = new Geometry[count];
        Array.Fill(whole, Geometry.Empty);
        whole[0] = outline;
        return whole;
    }

    static Piece[] Placed(Piece[] pieces)
    {
        Rect bounds = Rect.Empty;
        foreach (Piece piece in pieces)
        {
            bounds.Union(piece.Shape.Bounds);
            bounds.Union(piece.Cut.Bounds);
        }
        Transform placement = Placement(bounds);
        return pieces.Select(piece => piece with { Shape = Placed(piece.Shape, placement), Cut = Placed(piece.Cut, placement) }).ToArray();
    }

    static Geometry Placed(Geometry shape) => Placed(shape, Placement(shape.Bounds));

    static Geometry Placed(Geometry shape, Transform placement)
    {
        if (shape.IsEmpty()) return Geometry.Empty;

        var placed = new GeometryGroup { FillRule = FillRule.Nonzero, Children = { shape }, Transform = placement };
        placed.Freeze();
        return placed;
    }

    static Transform Placement(Rect bounds)
    {
        if (bounds.IsEmpty) return Transform.Identity;

        double scale = Math.Min(1, WidestSymbol / Math.Max(bounds.Width, bounds.Height));
        Matrix placement = Matrix.Identity;
        placement.Translate(-(bounds.Left + bounds.Width / 2), -(bounds.Top + bounds.Height / 2));
        placement.Scale(scale, scale);
        placement.Translate(GridSize / 2, GridSize / 2);
        return new MatrixTransform(placement);
    }

    static Geometry Union(Geometry a, Geometry b) =>
        Geometry.Combine(a, b, GeometryCombineMode.Union, null, Tolerance, ToleranceType.Absolute);

    static Geometry Exclude(Geometry a, Geometry b) =>
        Geometry.Combine(a, b, GeometryCombineMode.Exclude, null, Tolerance, ToleranceType.Absolute);

    static Geometry Intersect(Geometry a, Geometry b) =>
        Geometry.Combine(a, b, GeometryCombineMode.Intersect, null, Tolerance, ToleranceType.Absolute);
}
