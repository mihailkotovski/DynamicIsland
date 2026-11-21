using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    const double VolumeTrackWidth = 162;
    const double VolumeOvershoot = 7;
    const double VolumeOvershootSquash = 0.22;
    const double VolumeChangeThreshold = 0.004;
    const float VolumeUnknown = -1, VolumeMax = 0.999f, VolumeMin = 0.001f;
    const double QuietVolumeBelow = 0.34, MidVolumeBelow = 0.67;
    const double VolumeHudSeconds = 1.6, ChargeSeconds = 3, FocusSeconds = 2.2;
    const double ChargeFillWidth = 20;
    const int HeadsetLowPercent = 20, HeadsetCriticalPercent = 10;
    const string MutedLabel = "выкл";
    static readonly TimeSpan OvershootHold = TimeSpan.FromMilliseconds(140);
    static readonly CultureInfo Russian = new("ru-RU");

    readonly Spring _volumeOvershoot = new(0, 420, 18);
    readonly DelayedAction _overshootTimeout;
    float _lastVolume = VolumeUnknown;
    bool _lastMuted, _lastPlugged, _powerKnown;
    int _headsetCharge = Headset.Unknown;
    Guid _headsetId;
    bool? _doNotDisturb;

    int _clockGlowHour = -1;

    void UpdateClock()
    {
        DateTime now = DateTime.Now;
        IdleClock.Text = BigClock.Text = now.ToString("HH:mm");
        BigDate.Text = now.ToString("dddd, d MMMM", Russian);
        if (now.Hour != _clockGlowHour)
        {
            _clockGlowHour = now.Hour;
            Color tint = now.Hour switch
            {
                >= 6 and < 11 => Color.FromRgb(0xFF, 0xB3, 0x40),
                >= 11 and < 17 => Color.FromRgb(0x64, 0xD2, 0xFF),
                >= 17 and < 22 => Color.FromRgb(0xFF, 0x9F, 0x0A),
                _ => Color.FromRgb(0x7D, 0x7A, 0xFF),
            };
            var glow = new RadialGradientBrush(tint, tint.WithAlpha(0))
            {
                Center = new Point(0.18, 0.25),
                GradientOrigin = new Point(0.18, 0.25),
                RadiusX = 0.65,
                RadiusY = 0.9,
            };
            glow.Freeze();
            ClockGlow.Background = glow;
        }
    }

    void CheckFullscreen()
    {
        bool hidden = Settings.HideFullscreen && !_pickingArea && Native.IsForegroundFullscreen(_hwnd);
        if (hidden == _hiddenByFullscreen) return;
        _hiddenByFullscreen = hidden;
        UpdateTargets();
    }

    void PollVolume()
    {
        if (!_audio.TryGetVolume(out float level, out bool muted)) return;
        if (_audio.TryTakeSwitch(out AudioService.Output device))
        {
            _lastVolume = VolumeUnknown;
            ReadHeadset(device, true);
        }
        bool first = _lastVolume < 0;
        if (!first && Math.Abs(level - _lastVolume) < VolumeChangeThreshold && muted == _lastMuted) return;

        _lastVolume = level;
        _lastMuted = muted;

        int percent = (int)Math.Round(level * 100);
        VolIcon.Kind = InfoVolIcon.Kind = muted || percent == 0 ? Glyph.Mute : level < QuietVolumeBelow ? Glyph.Quiet : level < MidVolumeBelow ? Glyph.Mid : Glyph.Loud;
        VolText.Text = percent.ToString();
        InfoVol.Text = VolumeLabel(level, muted);
        VolFill.BeginAnimation(WidthProperty, new DoubleAnimation(muted ? 0 : VolumeTrackWidth * level, Ms(first ? 0 : 140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        if (first) return;
        ShowTransient(View.Volume, VolumeHudSeconds);
        ShowPlayerVolume(level, muted, false);
    }

    static string VolumeLabel(float level, bool muted) => muted ? MutedLabel : (int)Math.Round(level * 100) + "%";

    bool IsVolumeAtLimit(bool up) => _lastVolume >= 0 && (up ? !_lastMuted && _lastVolume >= VolumeMax : _lastVolume <= VolumeMin);

    void OvershootVolume(bool up)
    {
        VolTrack.RenderTransformOrigin = new Point(up ? 0 : 1, 0.5);
        _volumeOvershoot.Target = VolumeOvershoot;
        _overshootTimeout.Start(OvershootHold);
        ShowTransient(View.Volume, VolumeHudSeconds);
        ShowPlayerVolume(_lastVolume, _lastMuted, false);
        StartShapeLoop();
    }

    void ReleaseVolumeOvershoot()
    {
        _volumeOvershoot.Target = 0;
        StartShapeLoop();
    }

    void StretchVolumeBar()
    {
        double push = Math.Max(_volumeOvershoot.Value, -VolumeOvershoot);
        VolStretch.ScaleX = 1 + push / VolumeTrackWidth;
        VolStretch.ScaleY = 1 - push / VolumeOvershoot * VolumeOvershootSquash;
    }

    void ShowPlayerVolume(float level, bool muted, bool app)
    {
        if (_view != View.MediaBig) return;
        PlayerVolumeIcon.Kind = app ? Glyph.Note : VolIcon.Kind;
        PlayerVolumeIcon.Fill = PlayerVolumeText.Foreground = app ? _accentBrush : _dim;
        PlayerVolumeText.Text = VolumeLabel(level, muted);

        var show = new DoubleAnimationUsingKeyFrames { Duration = Ms(1900) };
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500))));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1900))));
        PlayerVolume.BeginAnimation(OpacityProperty, show);
    }

    void PollPower()
    {
        bool hasBattery = Native.TryGetBattery(out int percent, out bool plugged);
        InfoBatRow.SetVisible(hasBattery);
        if (!hasBattery) return;

        InfoBat.Text = percent + "%";
        InfoBat.Foreground = plugged ? _green : percent <= HeadsetLowPercent ? _red : Brushes.White;
        InfoBatIcon.Fill = plugged ? _green : percent <= HeadsetLowPercent ? _red : _dim;

        if (_powerKnown && plugged && !_lastPlugged) ShowCharging(percent);
        _powerKnown = true;
        _lastPlugged = plugged;
    }

    void ShowCharging(int percent)
    {
        ChargeText.Text = percent + "%";
        ChargeFill.BeginAnimation(WidthProperty, new DoubleAnimation(0, ChargeFillWidth * percent / 100.0, Ms(700))
        {
            BeginTime = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        ShowTransient(View.Charge, ChargeSeconds);

        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 };
        BoltTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-35, 0, Ms(520)) { BeginTime = FadeInDelay, EasingFunction = ease });
        BoltSize.AnimateScale(new DoubleAnimation(0.3, 1, Ms(520)) { BeginTime = FadeInDelay, EasingFunction = ease });
    }

    async void ReadHeadset(AudioService.Output? output, bool announce = false)
    {
        int level = output is { } bound ? await Headset.ChargeAsync(bound.Container) : Headset.Unknown;
        if (output != _audio.Device) return;

        AudioService.Output device = output ?? default;
        bool known = level >= 0, low = known && level <= HeadsetLowPercent;
        bool sameHeadset = device.Container == _headsetId;
        int was = sameHeadset ? _headsetCharge : Headset.Unknown;
        if (known || !sameHeadset) _headsetCharge = level;
        _headsetId = device.Container;

        Glyph icon = device.Headphones ? Glyph.Headphones : Glyph.Speaker;
        InfoHeadsetRow.SetVisible(known);
        PlayerHeadset.SetVisible(known);
        InfoHeadsetIcon.Kind = PlayerHeadsetIcon.Kind = icon;
        InfoHeadset.Text = PlayerHeadsetText.Text = level + "%";
        InfoHeadset.Foreground = low ? _red : Brushes.White;
        InfoHeadsetIcon.Fill = low ? _red : _dim;
        PlayerHeadsetIcon.Fill = PlayerHeadsetText.Foreground = low ? _red : _dim;
        if (output == null) return;

        string name = device.Name.Length > 0 ? device.Name : "Вывод звука";
        bool Passed(int mark) => was > mark && level <= mark;
        if (announce && known)
            ShowHeadset(name, HeadsetModel(device), level, low);
        else if (announce)
            Notify(icon, Brushes.White, device.Kind.Length > 0 ? device.Kind : "Аудиоустройство", name);
        else if (known && (Passed(HeadsetLowPercent) || Passed(HeadsetCriticalPercent)))
            ShowHeadset(name, HeadsetModel(device), level, true);
    }

    void PollDoNotDisturb()
    {
        if (IsFocusUnsettled || Native.IsDoNotDisturbOn() is not bool quiet || quiet == _doNotDisturb) return;
        bool first = _doNotDisturb == null;
        _doNotDisturb = quiet;
        InfoFocus.SetVisible(quiet);
        if (first) return;

        FocusIcon.Fill = FocusText.Foreground = quiet ? _indigo : _dim;
        FocusText.Text = quiet ? "Вкл." : "Выкл.";
        ShowTransient(View.Focus, FocusSeconds);

        IEasingFunction ease = quiet ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } : new CubicEase { EasingMode = EasingMode.EaseOut };
        var swing = new DoubleAnimation(quiet ? -80 : 0, quiet ? 0 : 24, Ms(quiet ? 620 : 420)) { BeginTime = FadeInDelay, EasingFunction = ease };
        var grow = new DoubleAnimation(quiet ? 0.4 : 1, quiet ? 1 : 0.84, Ms(quiet ? 520 : 420)) { BeginTime = FadeInDelay, EasingFunction = ease };
        FocusTurn.BeginAnimation(RotateTransform.AngleProperty, swing);
        FocusSize.AnimateScale(grow);
    }
}
