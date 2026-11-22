using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    const double SeekTrackWidth = 260;
    const double SeekHeight = 6, SeekDragHeight = 12, MinSeekHeight = 2;
    const double SeekSquashSpeed = 950, SeekMaxSquash = 0.3;
    const double SeekMaxOvershoot = 11, SeekOvershootResist = 42, SeekOvershootSquash = 0.24;
    const double SeekSettledFraction = 0.02;
    const double SeekHoldSeconds = 1;
    const int NoSeconds = -1;
    const string NoTime = "–:––";

    readonly Spring _seekFill = new(0, 170, 26), _seekHeight = new(SeekHeight, 420, 21);
    readonly Spring _seekSquash = new(0, 320, 22), _seekOvershoot = new(0, 380, 17);
    readonly FrameLoop _seekLoop;
    bool _scrubbing, _overshootRight;
    double _scrubFraction, _scrubHeldUntil;
    (int At, int Left) _seekLabel = (NoSeconds, NoSeconds);
    LyricsService.Line[] _seekLinesFrom = [];
    TimeSpan _seekLinesSpan;
    bool _seekLined;

    void StartSeekLoop()
    {
        _seekFill.Value = _seekFill.Velocity = 0;
        _seekSquash.Snap(0);
        _seekOvershoot.Snap(0);
        SeekScale.ScaleX = SeekScale.ScaleY = 1;
        _seekLoop.Start();
    }

    bool AdvanceSeek(double dt)
    {
        if (_view != View.MediaBig && !_scrubbing)
        {
            SeekScale.ScaleX = SeekScale.ScaleY = 1;
            return false;
        }

        double now = _clock.Elapsed.TotalSeconds;
        TimeSpan duration = _media.Duration;
        bool known = duration.TotalSeconds >= 1;
        bool counted = known || _media.HasBarPosition;
        double played = _media.Progress;
        if (!_scrubbing && now < _scrubHeldUntil && Math.Abs(played - _scrubFraction) < SeekSettledFraction) _scrubHeldUntil = 0;
        double shown = _scrubbing || now < _scrubHeldUntil ? _scrubFraction : played;

        double track = SeekArea.ActualWidth > 0 ? SeekArea.ActualWidth : SeekTrackWidth;
        Point? pointer = SeekArea.IsMouseOver || _scrubbing ? Mouse.GetPosition(SeekArea) : null;

        _seekFill.Target = track * shown;
        _seekFill.Advance(dt);

        _seekSquash.Target = _scrubbing ? Math.Clamp(Math.Abs(_seekFill.Velocity) / SeekSquashSpeed, 0, 1) : 0;
        _seekSquash.Advance(dt);

        _seekHeight.Target = _scrubbing ? SeekDragHeight * (1 - SeekMaxSquash * Math.Clamp(_seekSquash.Value, 0, 1)) : SeekHeight;
        _seekHeight.Advance(dt);

        double overshootTarget = 0;
        if (_scrubbing && pointer is { } atPointer)
        {
            if (atPointer.X < 0) overshootTarget = -SeekMaxOvershoot * (1 - Math.Exp(atPointer.X / SeekOvershootResist));
            else if (atPointer.X > track) overshootTarget = SeekMaxOvershoot * (1 - Math.Exp(-(atPointer.X - track) / SeekOvershootResist));
        }
        if (overshootTarget != 0) _overshootRight = overshootTarget > 0;
        _seekOvershoot.Target = overshootTarget;
        _seekOvershoot.Advance(dt);
        ApplySeekOvershoot(track);

        double bass = SeekBass(_media.IsPlaying);
        SeekPlain.Thickness = SeekLines.Thickness = Math.Max(_seekHeight.Value, MinSeekHeight);
        SeekPlain.Fill = SeekLines.Fill = Math.Clamp(_seekFill.Value, 0, track);
        SeekPlain.Advance(dt, pointer, _scrubbing, Settings.SeekHover, _seekFill.Velocity, _media.IsPlaying, bass);
        SeekLines.Advance(dt, pointer, _scrubbing, Settings.SeekHover, _seekFill.Velocity, _media.IsPlaying, bass);
        MarkLineStarts(duration);
        FillSungLine();
        AdvancePlayerLyrics(now, dt);

        TimeSpan at = known ? duration * shown : _media.Position;
        var label = counted ? ((int)at.TotalSeconds, known ? (int)(duration - at).TotalSeconds : NoSeconds) : (NoSeconds, NoSeconds);
        if (label == _seekLabel) return true;

        _seekLabel = label;
        PosText.Text = counted ? FormatTime(at) : NoTime;
        RemText.Text = known ? "-" + FormatTime(duration - at) : NoTime;
        return true;
    }

    void ApplySeekOvershoot(double track)
    {
        double push = Math.Clamp(_seekOvershoot.Value, -SeekMaxOvershoot * 1.4, SeekMaxOvershoot * 1.4);
        if (Math.Abs(push) <= 0.01)
        {
            SeekScale.ScaleX = SeekScale.ScaleY = 1;
            return;
        }
        SeekStretch.RenderTransformOrigin = new Point(_overshootRight ? 0 : 1, 0.5);
        double signed = _overshootRight ? push : -push;
        SeekScale.ScaleX = Math.Max(0.85, 1 + signed / track);
        SeekScale.ScaleY = Math.Max(0.65, 1 - signed / SeekMaxOvershoot * SeekOvershootSquash);
    }

    double SeekBass(bool playing)
    {
        if (!playing) return 0;
        int count = Math.Max(1, Eq.Bars / 3);
        double sum = 0;
        for (int b = 0; b < count; b++) sum += Eq.Level(b);
        return sum / count;
    }

    void MarkLineStarts(TimeSpan duration)
    {
        if (ReferenceEquals(_playerLines, _seekLinesFrom) && duration == _seekLinesSpan) return;

        _seekLinesFrom = _playerLines;
        _seekLinesSpan = duration;
        double seconds = duration.TotalSeconds;
        SeekLines.SetLines(seconds < 1 ? [] : _playerLines.Select(line => line.Time.TotalSeconds / seconds).ToArray(),
            _playerLines.Select(line => line.Text).ToArray());
        SyncSeekStyle(true);
    }

    void SyncSeekStyle(bool animate)
    {
        bool lined = Settings.LineBar && SeekLines.HasMarks;
        if (lined == _seekLined) return;

        _seekLined = lined;
        Duration time = Ms(animate ? 260 : 0);
        SeekLines.BeginAnimation(OpacityProperty, new DoubleAnimation(lined ? 1 : 0, time));
        SeekPlain.BeginAnimation(OpacityProperty, new DoubleAnimation(lined ? 0 : 1, time));
    }

    static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    double SeekFraction(MouseEventArgs e)
    {
        double x = e.GetPosition(SeekArea).X;
        if (_seekLined) x = SeekLines.Snap(x);
        return Math.Clamp(x / SeekArea.ActualWidth, 0, 1);
    }

    void Seek_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (SeekArea.ActualWidth <= 0 || !_media.Seekable) return;

        _scrubbing = true;
        _scrubFraction = SeekFraction(e);
        _seekFill.Tune(780, 48);
        PosText.Foreground = RemText.Foreground = Brushes.White;
        SeekArea.CaptureMouse();
    }

    void Seek_MouseMove(object sender, MouseEventArgs e)
    {
        if (_scrubbing) _scrubFraction = SeekFraction(e);
    }

    void Seek_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_scrubbing) return;

        EndScrub();
        _scrubHeldUntil = _clock.Elapsed.TotalSeconds + SeekHoldSeconds;
        _media.Seek(_scrubFraction);
        SeekArea.ReleaseMouseCapture();
    }

    void Seek_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_scrubbing) EndScrub();
    }

    void EndScrub()
    {
        _scrubbing = false;
        _seekFill.Tune(170, 26);
        PosText.Foreground = RemText.Foreground = _dim;
    }
}
