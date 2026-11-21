using System.Windows;
using System.Windows.Input;

namespace DynamicIsland;

public partial class MainWindow
{
    enum Grab { None, Held, Pull, Lean }

    const double DragThreshold = 5;
    const double DragFollow = 0.9;
    const double MaxPull = 96, MaxLean = 44;
    const double PullWidening = 0.4, PullRounding = 0.3;
    const double LeanStretch = 0.6;
    const double PullToOpen = 42, LeanToSkip = 40;
    const double FlickSpeed = 550, FlickMinDistance = 12;
    const double MaxThrowSpeed = 1000;
    const double PointerSpeedSmoothing = 0.03;
    const double PointerRestSeconds = 0.09;
    const double PointerSampleSeconds = 0.004;
    const float WheelVolumeStep = 0.02f;

    Grab _grab;
    Point _grabStart, _pointerLast;
    double _pointerAt;
    Vector _pointerSpeed;
    double _pull, _leanDrag;

    void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _hovered = true;
        _collapseTimeout.Cancel();
        UpdateTargets();
    }

    void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        _hovered = _pressed = false;
        StirLyrics();
        UpdateTargets();
        if (_panel != Panel.None) _collapseTimeout.Start(CollapseDelay);
    }

    void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        if (_panel == Panel.None && !_ringing && Island.CaptureMouse())
        {
            _grab = Grab.Held;
            _grabStart = _pointerLast = e.GetPosition(this);
            _pointerAt = _clock.Elapsed.TotalSeconds;
            _pointerSpeed = default;
        }
        UpdateTargets();
    }

    void Root_MouseMove(object sender, MouseEventArgs e)
    {
        StirLyrics();
        if (_grab == Grab.None) return;
        Point at = e.GetPosition(this);
        double now = _clock.Elapsed.TotalSeconds, dt = now - _pointerAt;
        if (dt >= PointerSampleSeconds)
        {
            _pointerSpeed += ((at - _pointerLast) / dt - _pointerSpeed) * (1 - Math.Exp(-dt / PointerSpeedSmoothing));
            _pointerLast = at;
            _pointerAt = now;
        }

        Vector moved = (at - _grabStart) / Math.Max(_userScale.Value, MinScale);
        if (_grab == Grab.Held)
        {
            if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold) return;
            _grab = Math.Abs(moved.X) > Math.Abs(moved.Y) ? Grab.Lean : Grab.Pull;
            if (_grab == Grab.Pull) _morphing = true;
        }
        if (_grab == Grab.Pull) _pull = Math.Max(moved.Y, 0);
        else _leanDrag = moved.X;
        UpdateTargets();
    }

    void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;

        Grab grab = _grab;
        double pulled = _pull, leant = _leanDrag;
        Vector pace = _clock.Elapsed.TotalSeconds - _pointerAt < PointerRestSeconds ? _pointerSpeed / Math.Max(_userScale.Value, MinScale) : default;
        ReleaseGrab();

        switch (grab)
        {
            case Grab.Lean:
                SkipByLean(leant, pace.X);
                break;
            case Grab.Pull when DragDirection(pulled, pace.Y, PullToOpen) <= 0:
                break;
            case Grab.Pull:
                ToggleOpen();
                ThrowOpen(pace.Y);
                break;
            default:
                ToggleOpen();
                break;
        }
        UpdateTargets();
    }

    void SkipByLean(double leant, double pace)
    {
        int way = -DragDirection(leant, pace, LeanToSkip);
        if (way == 0 || !IsMediaActive) return;

        RememberSkip(way);
        if (way > 0) _media.Next();
        else _media.Previous();
        _leanX.Velocity = Math.Clamp(pace, -MaxThrowSpeed, MaxThrowSpeed);
    }

    void ToggleOpen()
    {
        if (_view == View.Notice && _noticeOpen is { } open)
        {
            open();
            EndTransient();
            return;
        }
        if (_ringing || _panel != Panel.None) SelectPanel(Panel.None);
        else SelectPanel(IsMediaActive ? Panel.Player : _obs.Recording ? Panel.Record : _countdown.IsActive ? Panel.Timer : Panel.Player);
        UpdateView();
    }

    void ThrowOpen(double pace)
    {
        double thrown = Math.Clamp(pace, 0, MaxThrowSpeed);
        _height.Velocity = Math.Max(_height.Velocity, thrown);
        _width.Velocity = Math.Max(_width.Velocity, thrown / 2);
        _collapseTimeout.Start(LongCollapseDelay);
    }

    static int DragDirection(double by, double pace, double enough)
    {
        int way = Math.Sign(by);
        double far = Math.Abs(by), on = pace * way;
        if (far >= FlickMinDistance && on >= FlickSpeed) return way;
        return far >= enough && on > -FlickSpeed / 2 ? way : 0;
    }

    void ReleaseGrab()
    {
        if (_grab == Grab.None) return;
        bool dragged = _grab != Grab.Held;
        _grab = Grab.None;
        _pull = _leanDrag = 0;
        if (dragged) TuneDragSprings(false);
        Island.ReleaseMouseCapture();
    }

    void CancelGrab()
    {
        if (_grab == Grab.None) return;
        ReleaseGrab();
        _pressed = false;
    }

    void Island_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_grab == Grab.None) return;
        CancelGrab();
        UpdateTargets();
    }

    void Root_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        CancelGrab();
        SelectPanel(_panel == Panel.Menu ? Panel.None : Panel.Menu);
        UpdateSwitches(false);
        UpdateView();
    }

    void Root_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        CancelGrab();
        SelectPanel(Panel.None);
        _sentAway = true;
        _awayTimeout.Start(AwayDuration);
        UpdateView();
        UpdateTargets();
    }

    void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        bool up = e.Delta > 0;
        int step = up ? 1 : -1;
        e.Handled = true;
        if (Native.IsCtrlDown) SwitchSource(-step);
        else if (_view == View.TimerSet) SetMinutes(_setupMinutes + step);
        else if (_view == View.Look && SizeRow.IsMouseOver) SetScale(Settings.Scale + step * ScaleStep);
        else if (_view == View.Look && GapRow.IsMouseOver) SetGap(Settings.Gap + step * GapStep);
        else if (_view == View.Look && LookOverflow > 0) ScrollLook(-step);
        else if (_view == View.Shelf && ShelfOverflow > 0) ScrollShelf(-step);
        else if (_view == View.Update && UpdateNotesOverflow > 0) ScrollUpdateNotes(-step);
        else if (_view == View.MediaBig && Settings.AppVolume && _audio.AdjustAppVolume(_media.Source, step * WheelVolumeStep, out float level))
            ShowPlayerVolume(level, false, true);
        else if (IsVolumeAtLimit(up)) OvershootVolume(up);
        else _audio.AdjustVolume(step * WheelVolumeStep);
    }
}
