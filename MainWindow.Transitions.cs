using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    const double PageSlideDistance = 56;
    const double ViewBlur = 12;
    const double FadeOutScale = 0.9, FadeInScale = 0.86;
    static readonly TimeSpan RowStagger = TimeSpan.FromMilliseconds(28);
    static readonly TimeSpan FadeInDelay = TimeSpan.FromMilliseconds(70);
    static readonly Duration BackdropFadeIn = Ms(280), BackdropFadeOut = Ms(160);

    static TransformGroup CreateViewTransforms() => new() { Children = { new ScaleTransform(1, 1), new ScaleTransform(1, 1), new TranslateTransform() } };

    static ScaleTransform FadeScaleOf(FrameworkElement view) => (ScaleTransform)((TransformGroup)view.RenderTransform).Children[0];

    static ScaleTransform FitScaleOf(FrameworkElement view) => (ScaleTransform)((TransformGroup)view.RenderTransform).Children[1];

    static TranslateTransform OffsetOf(FrameworkElement view) => (TranslateTransform)((TransformGroup)view.RenderTransform).Children[2];

    static bool IsHidden(FrameworkElement view) => view.Visibility != Visibility.Visible || view.Opacity < BarelyVisible;

    static UIElementCollection? RowsOf(FrameworkElement view) => view is Grid { Children: [StackPanel list] } ? list.Children : null;

    bool IsCurrent(FrameworkElement view) => view == Shared ? MediaSpots.ContainsKey(_view) : _views[_view] == view;

    bool IsCurrentBackdrop(FrameworkElement backdrop) =>
        _backdrops.TryGetValue(_view, out FrameworkElement? current) && current == backdrop;

    void SwitchBackdrop(View target)
    {
        _backdrops.TryGetValue(target, out FrameworkElement? next);
        foreach (FrameworkElement backdrop in _backdrops.Values)
        {
            if (backdrop == next || backdrop.Visibility != Visibility.Visible) continue;
            HideBackdrop(backdrop);
        }
        if (next != null) ShowBackdrop(next);
    }

    static void ShowBackdrop(FrameworkElement backdrop)
    {
        backdrop.Visibility = Visibility.Visible;
        backdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(1, BackdropFadeIn)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    void HideBackdrop(FrameworkElement backdrop)
    {
        var fade = new DoubleAnimation(0, BackdropFadeOut)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) =>
        {
            if (IsCurrentBackdrop(backdrop)) return;
            backdrop.Visibility = Visibility.Collapsed;
        };
        backdrop.BeginAnimation(OpacityProperty, fade);
    }

    void SwitchView(FrameworkElement next, int direction)
    {
        foreach (FrameworkElement view in _views.Values)
        {
            if (view == next || view.Visibility != Visibility.Visible) continue;
            if (direction == 0) FadeOut(view);
            else SlideOut(view, direction);
        }
        if (direction == 0) FadeIn(next);
        else SlideIn(next, direction);
    }

    void SlideOut(FrameworkElement view, int direction)
    {
        view.IsHitTestVisible = false;
        view.Effect = null;
        OffsetOf(view).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-direction * PageSlideDistance, Ms(190))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn }
        });
        HideWhenFaded(view, Ms(150));
    }

    void SlideIn(FrameworkElement view, int direction)
    {
        view.Visibility = Visibility.Visible;
        view.IsHitTestVisible = true;
        view.Effect = null;
        FadeScaleOf(view).AnimateScale(null);

        var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
        TimeSpan wait = TimeSpan.FromMilliseconds(50), run = TimeSpan.FromMilliseconds(420), show = TimeSpan.FromMilliseconds(200);
        if (RowsOf(view) is not { } rows)
        {
            OffsetOf(view).BeginAnimation(TranslateTransform.XProperty, Delayed(direction * PageSlideDistance, 0, wait, run, ease));
            view.BeginAnimation(OpacityProperty, Delayed(0, 1, wait, show));
            return;
        }

        OffsetOf(view).BeginAnimation(TranslateTransform.XProperty, null);
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ms(100)));
        foreach (UIElement row in rows)
        {
            if (row.RenderTransform is not TranslateTransform move) row.RenderTransform = move = new TranslateTransform();
            move.BeginAnimation(TranslateTransform.XProperty, Delayed(direction * PageSlideDistance, 0, wait, run, ease));
            row.BeginAnimation(OpacityProperty, Delayed(0, 1, wait, show));
            wait += RowStagger;
        }
    }

    void FadeOut(FrameworkElement view)
    {
        view.IsHitTestVisible = false;
        view.AnimateBlur(0, ViewBlur, Ms(170), keep: true);
        FadeScaleOf(view).AnimateScale(new DoubleAnimation(FadeOutScale, Ms(170)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        HideWhenFaded(view, Ms(140));
    }

    void FadeIn(FrameworkElement view)
    {
        bool fresh = IsHidden(view);
        view.Visibility = Visibility.Visible;
        view.IsHitTestVisible = true;
        OffsetOf(view).BeginAnimation(TranslateTransform.XProperty, null);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        view.AnimateBlur(ViewBlur, 0, Ms(300), ease, FadeInDelay);
        var grow = new DoubleAnimation(1, Ms(380)) { BeginTime = FadeInDelay, EasingFunction = ease };
        if (fresh) grow.From = FadeInScale;
        FadeScaleOf(view).AnimateScale(grow);
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ms(240)) { BeginTime = FadeInDelay });
    }

    void HideWhenFaded(FrameworkElement view, Duration time)
    {
        var fade = new DoubleAnimation(0, time);
        fade.Completed += (_, _) =>
        {
            if (IsCurrent(view)) return;
            view.Visibility = Visibility.Collapsed;
            view.Effect = null;
        };
        view.BeginAnimation(OpacityProperty, fade);
    }
}
