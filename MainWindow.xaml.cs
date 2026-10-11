using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    enum View { Idle, Media, Timer, Record, Volume, Charge, Focus, Toast, Notice, MediaBig, IdleBig, TimerBig, TimerSet, RecordBig, RecordSet, Menu, Settings, Look, Shelf, Update, Loading }

    enum Panel { None, Player, Timer, TimerSet, Record, Menu, Settings, Look, Shelf, Update }

    readonly record struct PillShape(double Width, double Height, double Radius);

    static readonly Dictionary<View, PillShape> PillShapes = new()
    {
        [View.Idle] = new(118, 34, 17),
        [View.Media] = new(210, 34, 17),
        [View.Timer] = new(132, 34, 17),
        [View.Record] = new(132, 34, 17),
        [View.Volume] = new(250, 34, 17),
        [View.Charge] = new(230, 34, 17),
        [View.Focus] = new(236, 34, 17),
        [View.Toast] = new(340, 68, 30),
        [View.Notice] = new(320, 64, 29),
        [View.MediaBig] = new(380, PlayerHeight, 40),
        [View.IdleBig] = new(320, 124, 38),
        [View.TimerBig] = new(330, 92, 40),
        [View.TimerSet] = new(300, 190, 38),
        [View.RecordBig] = new(340, 92, 40),
        [View.RecordSet] = new(320, 92, 40),
        [View.Menu] = new(300, 288, 34),
        [View.Settings] = new(320, 374, 34),
        [View.Look] = new(352, LookHeight, 34),
        [View.Shelf] = new(380, 136, 34),
        [View.Update] = new(320, 150, 34),
        [View.Loading] = new(118, 34, 17),
    };

    readonly record struct MediaSpot(
        double CoverLeft, double CoverTop, double CoverSize,
        double BarsRight, double BarsCenterY, double BarsWidth, double BarsHeight, double BarsOpen)
    {
        public double[] Values => [CoverLeft, CoverTop, CoverSize, BarsRight, BarsCenterY, BarsWidth, BarsHeight, BarsOpen];
    }

    static readonly Dictionary<View, MediaSpot> MediaSpots = new()
    {
        [View.Media] = new(7, 6, 22, 13, 17, 21, 16, 0),
        [View.Toast] = new(12, 12, 44, 20, 34, 23, 20, 0),
        [View.MediaBig] = new(20, 20, 64, 22, 52, 38, 26, 1),
    };

    static readonly View[] MenuPages = [View.Settings, View.Look, View.TimerSet, View.TimerBig, View.RecordSet, View.RecordBig, View.Shelf];

    const double HostWidth = 620, HostHeight = 520;
    const double CompactMaxHeight = 40;
    const double BarelyVisible = 0.05;
    const double IntroScale = 0.3, IntroOffset = -50;
    const int FullscreenCheckTicks = 5, ClockUpdateTicks = 10, HeadsetReadTicks = 300;
    static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);
    static readonly TimeSpan PausedMediaLinger = TimeSpan.FromSeconds(30);
    static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(550);
    static readonly TimeSpan LongCollapseDelay = TimeSpan.FromSeconds(2.5);
    static readonly TimeSpan AwayDuration = TimeSpan.FromSeconds(5);

    readonly Dictionary<View, FrameworkElement> _views;
    readonly SolidColorBrush _dim, _orange, _green, _red, _indigo;
    readonly AudioService _audio = new();
    readonly MediaService _media;
    readonly LyricsService _lyrics = new();
    readonly NetworkService _network;
    readonly Updater _updater = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly DispatcherTimer _ticker = new() { Interval = TickInterval };
    readonly DelayedAction _transientTimeout, _collapseTimeout, _awayTimeout;
    readonly View? _forcedView;
    readonly double _forcedTimerSeconds;

    View _view = View.Idle;
    View? _transientView;
    Panel _panel;
    bool _hovered, _pressed, _hiddenByFullscreen, _sentAway;
    IntPtr _hwnd;
    int _ticks;

    public MainWindow()
    {
        InitializeComponent();
        Width *= LargestScale / 100.0;
        Height = Height * LargestScale / 100.0 + LargestGap;

        _dim = BrushResource("Dim");
        _orange = BrushResource("Orange");
        _green = BrushResource("Green");
        _red = BrushResource("Red");
        _indigo = BrushResource("Indigo");

        _views = new()
        {
            [View.Idle] = IdleView,
            [View.Media] = MediaView,
            [View.Timer] = TimerView,
            [View.Record] = RecordView,
            [View.Volume] = VolumeView,
            [View.Charge] = ChargeView,
            [View.Focus] = FocusView,
            [View.Toast] = ToastView,
            [View.Notice] = NoticeView,
            [View.MediaBig] = MediaBigView,
            [View.IdleBig] = IdleBigView,
            [View.TimerBig] = TimerBigView,
            [View.TimerSet] = TimerSetView,
            [View.RecordBig] = RecordBigView,
            [View.RecordSet] = RecordSetView,
            [View.Menu] = MenuView,
            [View.Settings] = SettingsView,
            [View.Look] = LookView,
            [View.Shelf] = ShelfView,
            [View.Update] = UpdatePage,
            [View.Loading] = LoadingView,
        };
        _spotSprings = [_coverLeft, _coverTop, _coverSize, _barsRight, _barsCenterY, _barsWidth, _barsHeight, _barsOpen];
        _springs =
        [
            _width, _height, _radius, _scale, _offsetY, _userScale, _topGap, _bubbleSplit, _bubbleScale, _bubbleRecord, _bubbleTimer, _bubbleShelf, _shelfBubbleWidth,
            _shelfScroll, _volumeOvershoot, _leanX, _coverScale, .. _spotSprings,
        ];
        _timerTint = new SolidColorBrush(_orange.Color);
        _recordTint = new SolidColorBrush(_red.Color);
        _lyricBlock = LyricA;

        _forcedView = Enum.TryParse(Argument("--view"), true, out View forced) ? forced : null;
        _forcedTimerSeconds = double.TryParse(Argument("--timer"), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) ? seconds : 0;

        _shapeLoop = new FrameLoop(AdvanceShape);
        _glass = new Glass(this);
        _eqLoop = new FrameLoop(AdvanceEq, EqFrameSeconds);
        _seekLoop = new FrameLoop(AdvanceSeek);
        _transientTimeout = new DelayedAction(EndTransient);
        _collapseTimeout = new DelayedAction(Collapse);
        _awayTimeout = new DelayedAction(ReturnFromAway);
        _overshootTimeout = new DelayedAction(ReleaseVolumeOvershoot);
        _dragLeaveTimeout = new DelayedAction(OnDragLeft);

        _media = new MediaService(Dispatcher);
        _media.Changed += OnMediaChanged;
        _lyrics.Changed += OnLyricsChanged;
        _network = new NetworkService(Dispatcher);
        _network.Changed += OnNetworkChanged;
        _updater.Changed += RefreshUpdatePage;
        _shelf = new Shelf(Dispatcher);
        _shelf.Changed += SyncShelf;
        _shelf.PictureLoaded += OnShelfPictureLoaded;
        _obs.Changed += OnObsChanged;
        _obs.Saved += OnRecordingSaved;

        PrepareViews();
        ApplyTimerTint();
        ApplyRecordTint();
        TuneDragSprings(false);
        _ticker.Tick += (_, _) => TickSafely();
        Loaded += OnLoaded;
    }

    void PrepareViews()
    {
        foreach (FrameworkElement view in _views.Values)
        {
            view.RenderTransformOrigin = new Point(0.5, 0.5);
            view.RenderTransform = CreateViewTransforms();
            view.Visibility = Visibility.Collapsed;
            view.Opacity = 0;
        }
        IdleView.Visibility = Visibility.Visible;
        IdleView.Opacity = 1;

        Shared.RenderTransform = CreateViewTransforms();
        ArtSpot.Clip = Squircle.Of(new Rect(0, 0, FullCoverSize, FullCoverSize), CoverRadius);
        LyricBox.CacheMode = _lyricCache;
        Eq.Fill = _accentBrush;
        Eq.Dots = Settings.Dots;
        SeekLines.Accent = _accentBrush;
        PlayerMatrix.Rounding = PillShapes[View.MediaBig].Radius;
        SyncBackdrop();
        foreach (FrameworkElement icon in new FrameworkElement[] { PlayIcon, PauseIcon, TimerPlayIcon, TimerPauseIcon })
        {
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = new ScaleTransform(1, 1);
        }
    }

    bool IsMediaActive => _media.HasTrack && (_media.IsPlaying || DateTime.UtcNow - _lastPlayedAt < PausedMediaLinger);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.HideFromTaskSwitcher(_hwnd);
        if (Native.RegisterShellHook(_hwnd)) HwndSource.FromHwnd(_hwnd).AddHook(OnShellMessage);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyShape();
    }

    static string? Argument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    SolidColorBrush BrushResource(string key) => (SolidColorBrush)FindResource(key);

    IntPtr OnShellMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (Native.MediaKeyOf(msg, wParam, lParam))
        {
            case Native.MediaKey.VolumeUp when IsVolumeAtLimit(true):
                OvershootVolume(true);
                break;
            case Native.MediaKey.VolumeDown when IsVolumeAtLimit(false):
                OvershootVolume(false);
                break;
            case Native.MediaKey.NextTrack:
                RememberSkip(1);
                NextIcon.Play();
                break;
            case Native.MediaKey.PreviousTrack:
                RememberSkip(-1);
                PrevIcon.Play();
                break;
        }
        return IntPtr.Zero;
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        CenterOnScreen();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(CenterOnScreen);
        UpdateClock();
        UpdateSwitches(false);
        RefreshLookPage();
        RefreshUpdatePage();
        SyncAccent(false);
        SyncShelf();
        SyncRecord();
        SyncGlass(false);
        PlayIntro();
        _ticker.Start();
        if (_forcedTimerSeconds > 0) StartTimer(TimeSpan.FromSeconds(_forcedTimerSeconds));
        if (_forcedView == View.Update) _ = _updater.CheckAsync();

        try { await _media.StartAsync(); }
        catch (Exception ex) { App.Log(ex); }
        try { await _network.StartAsync(); }
        catch (Exception ex) { App.Log(ex); }
        _obs.Start();
    }

    void CenterOnScreen()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
        _glass.Place();
    }

    void PlayIntro()
    {
        _scale.Value = IntroScale;
        _offsetY.Value = IntroOffset;
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(260)));
        UpdateView();
        UpdateTargets();
    }

    void Exit()
    {
        _ticker.Stop();
        _alarm.Stop();
        _glass.Hide();
        var fade = new DoubleAnimation(0, Ms(220));
        fade.Completed += (_, _) => Application.Current.Shutdown();
        Root.BeginAnimation(OpacityProperty, fade);
        _scale.Target = _bubbleScale.Target = 0.5;
        StartShapeLoop();
    }

    void TickSafely()
    {
        try
        {
            OnTick();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    void OnTick()
    {
        _ticks++;
        PollVolume();
        if (_ticks % HeadsetReadTicks == 1) ReadHeadset(_audio.Device);
        UpdateLyric();
        UpdateTimer();
        UpdateRecord();
        if (_ticks % FullscreenCheckTicks == 0)
        {
            CheckFullscreen();
            PollDoNotDisturb();
        }
        if (_ticks % ClockUpdateTicks == 0)
        {
            UpdateClock();
            PollPower();
            Native.KeepOnTop(_hwnd);
            _glass.Place();
            if (_media.IsPlaying) _lastPlayedAt = DateTime.UtcNow;
            UpdateView();
        }
    }

    View TargetView()
    {
        View target = _forcedView ?? _panel switch
        {
            Panel.Menu => View.Menu,
            Panel.Settings => View.Settings,
            Panel.Look => View.Look,
            Panel.Update => View.Update,
            Panel.Shelf => View.Shelf,
            Panel.TimerSet => View.TimerSet,
            Panel.Record => _obs.Recording ? View.RecordBig : View.RecordSet,
            Panel.Timer when _countdown.IsActive => View.TimerBig,
            Panel.Timer or Panel.Player => _media.HasTrack ? View.MediaBig : View.IdleBig,
            _ => _transientView ?? (_updater.State == Updater.Stage.Loading ? View.Loading
                : IsMediaActive ? View.Media : _obs.Recording ? View.Record : _countdown.IsActive ? View.Timer : View.Idle),
        };
        return target == View.Toast && !_media.HasTrack ? View.Idle : target;
    }

    void UpdateView()
    {
        View target = TargetView();
        bool changed = target != _view;
        if (changed) MorphTo(target);

        SyncEq();
        SyncRim();
        SyncCover();
        if (!changed) return;
        if (target == View.MediaBig) StartSeekLoop();
        if (target == View.Media) UpdateLyric(true);
    }

    void MorphTo(View target)
    {
        View previous = _view;
        PillShape from = ShapeOf(previous);
        _view = target;
        if (target == View.MediaBig) UpdatePlayerLyrics(true);
        else ShowLyricsPlaceholder(false);
        PillShape to = ShapeOf(target);
        bool growing = to.Width * to.Height >= from.Width * from.Height;
        _width.Tune(growing ? 330 : 260, growing ? 22 : 27);
        _height.Tune(growing ? 220 : 400, growing ? 22 : 33);
        foreach (Spring spring in _spotSprings) spring.Tune(_height.Stiffness, _height.Damping);

        _morphing = true;
        SwitchView(_views[target], MenuDirection(previous, target));
        MoveMediaSpot(previous, target, to);
        UpdateTargets();
    }

    void MoveMediaSpot(View from, View to, PillShape size)
    {
        bool had = MediaSpots.ContainsKey(from), has = MediaSpots.TryGetValue(to, out MediaSpot spot);
        if (has)
        {
            bool fresh = IsHidden(Shared);
            double[] places = spot.Values;
            for (int i = 0; i < _spotSprings.Length; i++)
            {
                if (fresh) _spotSprings[i].Snap(places[i]);
                else _spotSprings[i].Target = places[i];
            }
        }
        ArtSpot.Cursor = to == View.MediaBig ? Cursors.Hand : null;
        if (has == had) return;

        ScaleTransform scale = FadeScaleOf(Shared);
        scale.CenterX = HostWidth / 2;
        scale.CenterY = (has ? size : ShapeOf(from)).Height / 2;
        if (has) FadeIn(Shared);
        else FadeOut(Shared);
    }

    static int MenuDirection(View from, View to) =>
        from == View.Menu && MenuPages.Contains(to) || (from, to) == (View.Settings, View.Update) ? 1
        : to == View.Menu && MenuPages.Contains(from) || (from, to) == (View.Update, View.Settings) ? -1 : 0;

    PillShape ShapeOf(View view) => view switch
    {
        View.Media => PillShapes[view] with { Width = _compactMediaWidth },
        View.MediaBig when _playerHasLyricRoom => PillShapes[view] with { Height = PlayerHeight + PlayerLyricsHeight },
        View.RecordSet => PillShapes[view] with { Width = RecordSetView.Width },
        View.Update => PillShapes[view] with { Height = UpdatePage.Height },
        View.Look => PillShapes[view] with { Height = LookView.Height },
        _ => PillShapes[view],
    };

    void ShowTransient(View view, double seconds, bool force = false)
    {
        if (force) _panel = Panel.None;
        else if (_panel != Panel.None || _hiddenByFullscreen || _sentAway) return;
        _transientView = view;
        _transientTimeout.Start(TimeSpan.FromSeconds(seconds));
        UpdateView();
    }

    void EndTransient()
    {
        _transientView = null;
        SilenceAlarm();
        UpdateView();
    }

    void SelectPanel(Panel panel)
    {
        _panel = panel;
        _transientView = null;
        _transientTimeout.Cancel();
        SilenceAlarm();
    }

    void ShowPanel(Panel panel)
    {
        _panel = panel;
        UpdateView();
    }

    void Collapse()
    {
        if (_hovered || _pickingFiles) return;
        ShowPanel(Panel.None);
    }

    void ReturnFromAway()
    {
        _sentAway = false;
        UpdateTargets();
    }
}
