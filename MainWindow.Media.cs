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
    readonly Spring _coverLeft = new(0), _coverTop = new(0), _coverSize = new(FullCoverSize);
    readonly Spring _barsRight = new(0), _barsCenterY = new(0), _barsWidth = new(1), _barsHeight = new(1), _barsOpen = new(0);
    readonly Spring _coverScale = new(1);
    readonly Spring[] _spotSprings;
    readonly FrameLoop _eqLoop;
    ImageSource? _cover;
    Color? _rimColor;
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
        bool music = _media.HasTrack && (_cover != null || Settings.Accent != null) && (IsMediaActive || _view == View.MediaBig);
        Color? tint = Settings.Rim && music ? AccentColor : null;
        if (tint == _rimColor) return;
        _rimColor = tint;
        Body.Tint(tint, TintTime);
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
        _accentBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(AccentColor, time));
        Color[] palette = Settings.Accent is { } own ? CoverPalette.Around(own) : _media.Palette;
        Glow.Tint(palette, time);
        PlayerMatrix.Tint(palette, time);
        PlayerLyricWait.Tint(palette, time);
        foreach (RadioButton tile in BackdropStrip.Children) ((BackdropPreview)tile.Content).Tint(palette);
    }

    void SyncBackdrop()
    {
        Backdrop backdrop = Settings.Backdrop;
        Glow.SetVisible(backdrop == Backdrop.Glow);
        PlayerMatrix.SetVisible(backdrop.HasFlag(Backdrop.Matrix));
        PlayerStars.SetVisible(backdrop.HasFlag(Backdrop.Stars));
        PlayerStars.AboveMatrix = backdrop.HasFlag(Backdrop.Matrix);
    }

    void SyncEq()
    {
        _spectrum.Source = Settings.AppSpectrum ? _media.Source : "";
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
        moving |= Settings.Backdrop == Backdrop.Glow ? Glow.Tick(Eq, now, dt) : AdvanceBackdrop(bands, playing, now, dt);
        return IsEqVisible && (playing || moving);
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
        if (_view == View.Media) MediaView.Width = w;
        if (_view != View.MediaBig) return;

        MediaBigView.Height = Math.Max(h, PlayerHeight);
        PlayerLyricBox.Opacity = Math.Clamp((h - PlayerHeight) / PlayerLyricsHeight * 2 - 1, 0, 1);
    }

    void PlaceCoverAndBars(Rect pill, double dpi)
    {
        double art = Math.Max(_coverSize.Value, 1), back = art * (1 - _coverScale.Value) / 2;
        ArtSize.ScaleX = ArtSize.ScaleY = Math.Max(art * _coverScale.Value, 1) / FullCoverSize;
        ArtMove.X = Math.Round((pill.Left + _coverLeft.Value) * dpi) / dpi + back;
        ArtMove.Y = Math.Round(_coverTop.Value * dpi) / dpi + back;

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
