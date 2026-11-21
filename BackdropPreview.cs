using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

sealed class BackdropPreview : FrameworkElement
{
    const double FrameSeconds = 1.0 / 30;
    const double Rounding = 8;
    const double BeatsPerSecond = 1.9, BeatSharpness = 3;
    const double QuietLevel = 0.18, BeatGain = 0.6, HighsFalloff = 0.55, RippleGain = 0.2, RippleSpeed = 3.1, RippleSpread = 7;

    const int MatrixRows = 3;
    const double ColumnPitch = 5.5, RowPitch = 4.5, DotRadius = 1.1, MatrixBottom = 5, MatrixInset = 5;
    const double LitReach = 1.1, UnlitOpacity = 0.1, LitOpacity = 0.9;

    const int StarCount = 14, StarSeed = 3;
    const double StarMargin = 5, SkyOverMatrix = 17;
    const double SmallestStar = 0.4, StarSpread = 0.55, DimmestStar = 0.25, StarBrightening = 0.45;
    const double SlowestTwinkle = 1.1, TwinkleSpread = 2.4, TwinkleDepth = 0.6, StarFlare = 0.5;

    const double GlowBaseOpacity = 0.3, GlowOpacityGain = 0.55;
    const double GlowBaseWidth = 0.3, GlowWidthGain = 0.1, GlowBaseHeight = 0.4, GlowHeightGain = 0.45;

    readonly record struct Star(double X, double Y, double Radius, double Glow, double Share, double Speed, double Phase);

    static readonly Star[] Stars = Scatter();
    static readonly double[] GlowCenters = [0.22, 0.52, 0.82];

    readonly FrameLoop _loop;
    readonly List<Shades> _shades = [];
    RadialGradientBrush[] _glows = [];
    IReadOnlyList<Color> _colors = CoverPalette.Plain;
    double _time;

    public BackdropPreview()
    {
        _loop = new FrameLoop(Advance, FrameSeconds);
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _loop.Start();
        };
    }

    public Backdrop Kind { get; set; }

    public void Tint(IReadOnlyList<Color> colors)
    {
        _colors = colors;
        _glows = [.. GlowCenters.Select((_, patch) => new RadialGradientBrush(colors[patch % colors.Count], colors[patch % colors.Count].WithAlpha(0)))];
        InvalidateVisual();
    }

    bool Advance(double dt)
    {
        _time += dt;
        InvalidateVisual();
        return IsVisible;
    }

    double Level(double share)
    {
        double beat = Math.Pow(0.5 + 0.5 * Math.Sin(2 * Math.PI * BeatsPerSecond * _time), BeatSharpness);
        double ripple = Math.Sin(_time * RippleSpeed + share * RippleSpread);
        return Math.Clamp(QuietLevel + BeatGain * beat * (1 - HighsFalloff * share) + RippleGain * ripple, 0, 1);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), Rounding, Rounding));
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
        if (Kind == Backdrop.Glow) DrawGlow(dc, w, h);
        if (Kind == Backdrop.Cover) DrawCover(dc, w, h);
        if (Kind.HasFlag(Backdrop.Stars)) DrawStars(dc, w, h - (Kind.HasFlag(Backdrop.Matrix) ? SkyOverMatrix : 0));
        if (Kind.HasFlag(Backdrop.Matrix)) DrawMatrix(dc, w, h);
        dc.Pop();
    }

    void DrawCover(DrawingContext dc, double w, double h)
    {
        for (int patch = 0; patch < _glows.Length; patch++)
        {
            double angle = _time * (0.45 + 0.15 * patch) + patch * 2.1;
            double cx = w * (0.5 + 0.28 * Math.Cos(angle));
            double cy = h * (0.5 + 0.26 * Math.Sin(angle * 0.85));
            _glows[patch].Opacity = 0.55;
            dc.DrawEllipse(_glows[patch], null, new Point(cx, cy), w * 0.52, h * 0.62);
        }
    }

    void DrawGlow(DrawingContext dc, double w, double h)
    {
        for (int patch = 0; patch < _glows.Length; patch++)
        {
            double level = Level((double)patch / _glows.Length);
            _glows[patch].Opacity = GlowBaseOpacity + GlowOpacityGain * level;
            dc.DrawEllipse(_glows[patch], null, new Point(w * GlowCenters[patch], h),
                w * (GlowBaseWidth + GlowWidthGain * level), h * (GlowBaseHeight + GlowHeightGain * level));
        }
    }

    void DrawStars(DrawingContext dc, double w, double h)
    {
        foreach (Star star in Stars)
        {
            double twinkle = 0.5 + 0.5 * Math.Sin(_time * star.Speed + star.Phase);
            double glow = Math.Min(star.Glow * (1 - TwinkleDepth * twinkle) + StarFlare * Level(star.Share) * star.Glow, 1);
            var center = new Point(StarMargin + (w - 2 * StarMargin) * star.X, StarMargin + (h - 2 * StarMargin) * star.Y);
            dc.DrawEllipse(Shades.White(glow), null, center, star.Radius, star.Radius);
        }
    }

    void DrawMatrix(DrawingContext dc, double w, double h)
    {
        int count = (int)((w - 2 * MatrixInset) / ColumnPitch) + 1;
        if (count % 2 == 0) count--;
        double left = (w - (count - 1) * ColumnPitch) / 2, middle = Math.Max((count - 1) / 2.0, 1);
        while (_shades.Count < count) _shades.Add(new Shades());

        for (int i = 0; i < count; i++)
        {
            double share = Math.Abs(i - middle) / middle, lit = Level(share) * MatrixRows * LitReach;
            Color color = Along(share);
            for (int row = 0; row < MatrixRows; row++)
            {
                var center = new Point(left + i * ColumnPitch, h - MatrixBottom - row * RowPitch);
                double on = Math.Clamp(lit - row, 0, 1);
                dc.DrawEllipse(Shades.White(UnlitOpacity), null, center, DotRadius, DotRadius);
                if (on > 0) dc.DrawEllipse(_shades[i].Of(color, LitOpacity * on), null, center, DotRadius, DotRadius);
            }
        }
    }

    Color Along(double share) => _colors[Math.Min((int)(share * _colors.Count), _colors.Count - 1)];

    static Star[] Scatter()
    {
        var random = new Random(StarSeed);
        var stars = new Star[StarCount];
        for (int i = 0; i < StarCount; i++)
        {
            double size = random.NextDouble();
            stars[i] = new Star(
                random.NextDouble(),
                random.NextDouble(),
                SmallestStar + StarSpread * size,
                DimmestStar + StarBrightening * size,
                random.NextDouble(),
                SlowestTwinkle + TwinkleSpread * random.NextDouble(),
                2 * Math.PI * random.NextDouble());
        }
        return stars;
    }
}
