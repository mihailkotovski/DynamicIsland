using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    const double FullCoverSize = 64, CoverRadius = 16;
    const double PausedCoverScale = 0.85;
    const double SkipMemorySeconds = 3;
    const double SourceSwitchCooldown = 0.25;
    const double EqFrameSeconds = 0.012;
    const double ToastSeconds = 3.2;
    const double PeakFallback = 0.4;
    const double IconBlur = 6;
    const string UnknownArtist = "Неизвестный исполнитель";
    static readonly Duration TintTime = Ms(450);

    readonly SpectrumService _spectrum = new();
    readonly float[] _bands = new float[SpectrumService.Bands];
    readonly SpectrumLevels _backdropLevels = new();
    readonly SolidColorBrush _accentBrush = new(Colors.White);
    readonly RadialGradientBrush _artGlowBrush = CreateArtGlowBrush();
    readonly Spring _coverLeft = new(0), _coverTop = new(0), _coverSize = new(FullCoverSize);
    readonly Spring _barsRight = new(0), _barsCenterY = new(0), _barsWidth = new(1), _barsHeight = new(1), _barsOpen = new(0);
    readonly Spring _coverScale = new(1);
    readonly Spring _coverSnapX = new(0, 200, 28), _coverSnapY = new(0, 200, 28);
    readonly Spring[] _spotSprings;
    readonly FrameLoop _eqLoop;
    ImageSource? _cover;
    Color[]? _rimPalette;
    string _lastTitle = "";
    DateTime _lastPlayedAt = DateTime.MinValue;
    bool _shownPlaying;
    int _skipDirection = 1;
    double _skipAt = -SkipMemorySeconds;
    double _sourceSwitchedAt = -SkipMemorySeconds;
    string _source = "", _sourceName = "";

    Color AccentColor => Settings.Accent ?? _media.Accent;

    bool IsEqVisible => MediaSpots.ContainsKey(_view);

    void OnMediaChanged()
    {
        string title = _media.HasTrack ? _media.Name : "";
        bool newTrack = title.Length > 0 && title != _lastTitle;
        _lastTitle = title;

        bool asked = _clock.Elapsed.TotalSeconds - _sourceSwitchedAt < SkipMemorySeconds;
        string source = _media.Source;
        bool turned = source != _source && asked;
        if (source != _source) _sourceName = asked ? SourceApp.Name(source) : "";
        else if (newTrack && !asked) _sourceName = "";
        _source = source;

        string artist = string.IsNullOrWhiteSpace(_media.Artist) ? UnknownArtist : _media.Artist;
        TitleBig.Text = ToastTitle.Text = title;
        ArtistBig.Text = ToastArtist.Text = _sourceName.Length > 0 ? _sourceName + " · " + artist : artist;

        if (!ReferenceEquals(_cover, _media.Art))
        {
            _cover = _media.Art;
            int heading = _clock.Elapsed.TotalSeconds - _skipAt < SkipMemorySeconds ? _skipDirection : 1;
            Art.Show(_cover, heading);
            PlayerCover.SetArt(_cover, MediaBigView.IsVisible);
            SyncAccent();
        }

        if (_media.IsPlaying != _shownPlaying)
        {
            _shownPlaying = _media.IsPlaying;
            SwapIcons(_shownPlaying ? PlayIcon : PauseIcon, _shownPlaying ? PauseIcon : PlayIcon, MediaBigView.IsVisible);
        }
        if (_media.IsPlaying || turned) _lastPlayedAt = DateTime.UtcNow;

        if (turned || (newTrack && _media.IsPlaying)) ShowTransient(View.Toast, ToastSeconds);
        TrackLyrics();
        UpdateView();
        UpdateLyric();
    }

    static void SwapIcons(FrameworkElement leave, FrameworkElement enter, bool animate)
    {
        Duration quick = Ms(animate ? 150 : 0), slow = Ms(animate ? 360 : 0);

        leave.RenderTransform.AnimateScale(new DoubleAnimation(0.5, quick) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        leave.BeginAnimation(OpacityProperty, new DoubleAnimation(0, quick));

        enter.RenderTransform.AnimateScale(new DoubleAnimation(0.5, 1, slow) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } });
        enter.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ms(animate ? 200 : 0)));

        leave.Effect = enter.Effect = null;
        if (!animate) return;

        leave.AnimateBlur(0, IconBlur, quick);
        enter.AnimateBlur(IconBlur, 0, Ms(240));
    }

    void RememberSkip(int direction)
    {
        _skipDirection = direction;
        _skipAt = _clock.Elapsed.TotalSeconds;
    }

    void SwitchSource(int direction)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (now - _sourceSwitchedAt < SourceSwitchCooldown || !_media.SwitchSession(direction)) return;
        _sourceSwitchedAt = now;
        RememberSkip(direction);
    }

    void SyncRim()
    {
        Color[]? palette = ContextualRimPalette();
        if (SamePalette(_rimPalette, palette)) return;
        _rimPalette = palette;
        Body.Tint(palette, TintTime);
    }

    Color[]? ContextualRimPalette()
    {
        if (!Settings.Rim) return null;
        return _view switch
        {
            View.Media or View.Toast or View.MediaBig when _media.HasTrack && (_cover != null || Settings.Accent != null)
                => Settings.Accent is { } own ? CoverPalette.Around(own) : _media.Palette,
            View.Timer or View.TimerBig or View.TimerSet => [_timerTint.Color],
            View.Record or View.RecordBig => [_recordTint.Color],
            View.Weather or View.Place when _weather.Now is { } report => [WeatherColor(report.Sky, report.Night)],
            View.Charge => [_green.Color],
            View.Focus when _doNotDisturb == true => [_indigo.Color],
            _ => null,
        };
    }

    static bool SamePalette(Color[]? a, Color[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    static RadialGradientBrush CreateArtGlowBrush()
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Colors.White.WithAlpha(0.75), 0));
        brush.GradientStops.Add(new GradientStop(Colors.White.WithAlpha(0.32), 0.52));
        brush.GradientStops.Add(new GradientStop(Colors.White.WithAlpha(0), 1));
        return brush;
    }

    void SyncCover()
    {
        bool player = _view == View.MediaBig;
        double to = player && !_media.IsPlaying ? PausedCoverScale : 1;
        if (to == _coverScale.Target) return;
        bool lively = player && to == 1;
        _coverScale.Tune(lively ? 260 : 240, lively ? 13 : 25);
        if (IsHidden(Shared)) _coverScale.Snap(to);
        else _coverScale.Target = to;
        StartShapeLoop();
    }

    void SyncAccent(bool animate = true)
    {
        Duration time = animate ? TintTime : Ms(0);
        Color accent = AccentColor;
        _accentBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(accent, time));
        _artGlowBrush.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(accent.WithAlpha(0.75), time));
        _artGlowBrush.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(accent.WithAlpha(0.32), time));
        _artGlowBrush.GradientStops[2].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(accent.WithAlpha(0), time));
        Color[] palette = Settings.Accent is { } own ? CoverPalette.Around(own) : _media.Palette;
        Glow.Tint(palette, time);
        MiniWash.Tint(palette, time);
        PlayerMatrix.Tint(palette, time);
        PlayerLyricWait.Tint(palette, time);
        foreach (RadioButton tile in BackdropStrip.Children) ((BackdropPreview)tile.Content).Tint(palette);
    }

    void SyncBackdrop()
    {
        Backdrop backdrop = Settings.Backdrop;
        PlayerCover.SetVisible(backdrop == Backdrop.Cover);
        Glow.SetVisible(backdrop == Backdrop.Glow);
        PlayerMatrix.SetVisible(backdrop.HasFlag(Backdrop.Matrix));
        PlayerStars.SetVisible(backdrop.HasFlag(Backdrop.Stars));
        PlayerStars.AboveMatrix = backdrop.HasFlag(Backdrop.Matrix);
    }

    void SyncEq()
    {
        _spectrum.Active = IsEqVisible && _media.IsPlaying;
        if (IsEqVisible) _eqLoop.Start();
    }

    bool AdvanceEq(double dt)
    {
        double now = _clock.Elapsed.TotalSeconds;
        bool playing = _media.IsPlaying;
        float[]? bands = _spectrum.Read(_bands) ? _bands : null;
        double level = 0;
        if (bands == null)
        {
            float peak = _audio.Peak();
            level = peak < 0 ? PeakFallback : peak;
        }

        bool moving = Eq.Tick(bands, level, playing, now, dt);
        moving |= Settings.Backdrop switch
        {
            Backdrop.Glow => Glow.Tick(Eq, now, dt),
            Backdrop.Cover => PlayerCover.Tick(Eq, playing, now, dt),
            _ => AdvanceBackdrop(bands, playing, now, dt),
        };
        moving |= UpdateMiniProgress(playing, now, dt);
        return IsEqVisible && (playing || moving);
    }

    bool UpdateMiniProgress(bool playing = false, double now = 0, double dt = 0.016)
    {
        CompactProgress style = Settings.CompactProgress;
        bool hasProgress = _media.HasTrack && _media.Duration.TotalSeconds >= 1;
        double progress = hasProgress ? Math.Clamp(_media.Progress, 0, 1) : 0;

        bool wash = hasProgress && style == CompactProgress.Wash && (_view == View.Media || MiniWash.Visibility == Visibility.Visible);
        bool washMoving = MiniWash.Tick(Eq, progress, wash, playing, now, dt);

        bool ring = _view == View.Media && hasProgress && style == CompactProgress.Ring;
        if (ring) ArtRing.Progress = progress;
        UpdateArtRingOpacity(Math.Max(_coverSize.Value, 1));
        return washMoving;
    }

    void UpdateArtRingOpacity(double artSize)
    {
        bool ring = Settings.CompactProgress == CompactProgress.Ring && _view == View.Media
            && _media.HasTrack && _media.Duration.TotalSeconds >= 1;
        double compact = Math.Clamp(1 - (artSize - 22) / 14, 0, 1);
        ArtRing.Opacity = ring ? compact : 0;
    }

    bool AdvanceBackdrop(float[]? bands, bool playing, double now, double dt)
    {
        _backdropLevels.Follow(bands, Eq, playing, dt);
        Backdrop backdrop = Settings.Backdrop;
        bool moving = backdrop.HasFlag(Backdrop.Matrix) && PlayerMatrix.Tick(_backdropLevels);
        if (backdrop.HasFlag(Backdrop.Stars)) moving |= PlayerStars.Tick(_backdropLevels, playing, now, dt);
        return moving;
    }

    void FitMediaViews(double w, double h)
    {
        if (_view == View.Media)
        {
            MediaView.Width = w;
            MediaView.Height = Math.Max(h, PillShapes[View.Media].Height);
            CentreLyrics();
        }
        if (_view != View.MediaBig) return;

        MediaBigView.Height = Math.Max(h, PlayerHeight);
        PlayerLyricBox.Opacity = Math.Clamp((h - PlayerHeight) / PlayerLyricsHeight * 2 - 1, 0, 1);
    }

    void AimCoverAtWholePixels(MediaSpot rest, double restWidth)
    {
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
        double left = (HostWidth - restWidth) / 2 + rest.CoverLeft;
        _coverSnapX.Target = Math.Round(left * dpi) / dpi - left;
        _coverSnapY.Target = Math.Round(rest.CoverTop * dpi) / dpi - rest.CoverTop;
    }

    void PlaceCoverAndBars(Rect pill)
    {
        double art = Math.Max(_coverSize.Value, 1), back = art * (1 - _coverScale.Value) / 2;
        ArtSize.ScaleX = ArtSize.ScaleY = Math.Max(art * _coverScale.Value, 1) / FullCoverSize;
        ArtMove.X = pill.Left + _coverLeft.Value + _coverSnapX.Value + back;
        ArtMove.Y = _coverTop.Value + _coverSnapY.Value + back;

        ArtRingMove.X = pill.Left + _coverLeft.Value + _coverSnapX.Value + (art - ArtRing.Width) / 2;
        ArtRingMove.Y = _coverTop.Value + _coverSnapY.Value + (art - ArtRing.Height) / 2;
        UpdateArtRingOpacity(art);

        double glowAlpha = Math.Clamp(_barsOpen.Value, 0, 1) * 0.34 * (_cover != null ? _coverScale.Value : 0);
        ArtGlow.Opacity = glowAlpha;
        ArtGlowScale.ScaleX = ArtGlowScale.ScaleY = Math.Max(_coverScale.Value, 0.5);
        ArtGlowMove.X = pill.Left + _coverLeft.Value + (art - ArtGlow.Width) / 2;
        ArtGlowMove.Y = _coverTop.Value + (art - ArtGlow.Height) / 2 + 4;

        double eqW = Math.Max(_barsWidth.Value, 1), eqH = Math.Max(_barsHeight.Value, 1);
        Eq.Width = eqW;
        Eq.Height = eqH;
        Eq.Open = _barsOpen.Value;
        EqMove.X = pill.Right - _barsRight.Value - eqW;
        EqMove.Y = _barsCenterY.Value - eqH / 2;
    }

    void Play_Click(object sender, RoutedEventArgs e) => _media.TogglePlay();

    void Prev_Click(object sender, RoutedEventArgs e)
    {
        RememberSkip(-1);
        PrevIcon.Play();
        _media.Previous();
    }

    void Next_Click(object sender, RoutedEventArgs e)
    {
        RememberSkip(1);
        NextIcon.Play();
        _media.Next();
    }

    void Art_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressed && _view == View.MediaBig) SourceApp.BringToFront(_media.Source, _media.Title);
    }
}
