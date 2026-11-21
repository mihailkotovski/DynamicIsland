using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public sealed class Digits : ContentControl
{
    readonly StackPanel _cells = new() { Orientation = Orientation.Horizontal };
    readonly List<TextBlock?> _glyphs = [];
    string _text = "";
    bool _rollingDown = true;

    public Digits()
    {
        Focusable = false;
        IsTabStop = false;
        UseLayoutRounding = false;
        Content = _cells;
    }

    public bool? RollDown { get; set; }

    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (value == _text) return;
            _rollingDown = RollDown ?? IsDecrease(_text, value) ?? _rollingDown;
            _text = value;
            Render(value, IsVisible);
        }
    }

    static bool? IsDecrease(string was, string next)
    {
        if (!was.Any(char.IsAsciiDigit) || !next.Any(char.IsAsciiDigit)) return null;
        string a = string.Concat(was.Where(char.IsAsciiDigit)).TrimStart('0');
        string b = string.Concat(next.Where(char.IsAsciiDigit)).TrimStart('0');
        int order = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
        return order == 0 ? null : order > 0;
    }

    double TravelDistance => Math.Round(FontSize * 0.48);
    double BlurRadius => Math.Clamp(FontSize * 0.2, 1.5, 9);

    void Render(string text, bool animate)
    {
        while (_glyphs.Count > text.Length)
        {
            _cells.Children.RemoveAt(0);
            _glyphs.RemoveAt(0);
        }
        while (_glyphs.Count < text.Length)
        {
            _cells.Children.Insert(0, new Grid());
            _glyphs.Insert(0, null);
        }

        for (int i = 0; i < text.Length; i++)
        {
            string symbol = text[i].ToString();
            TextBlock? old = _glyphs[i];
            if (old?.Text == symbol) continue;

            var cell = (Grid)_cells.Children[i];
            var next = new TextBlock { Text = symbol, RenderTransform = new TranslateTransform() };
            Typography.SetNumeralAlignment(next, FontNumeralAlignment.Tabular);
            _glyphs[i] = next;

            if (!animate) cell.Children.Clear();
            cell.Children.Add(next);
            if (!animate) continue;

            if (old != null) AnimateOut(cell, old);
            AnimateIn(next);
        }
    }

    void AnimateOut(Grid cell, TextBlock glyph)
    {
        glyph.AnimateBlur(0, BlurRadius, Ms(220), keep: true);
        ((TranslateTransform)glyph.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(_rollingDown ? TravelDistance : -TravelDistance, Ms(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });

        var fade = new DoubleAnimation(0, Ms(200));
        fade.Completed += (_, _) => cell.Children.Remove(glyph);
        glyph.BeginAnimation(OpacityProperty, fade);
    }

    void AnimateIn(TextBlock glyph)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        glyph.AnimateBlur(BlurRadius, 0, Ms(320), ease);
        ((TranslateTransform)glyph.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(_rollingDown ? -TravelDistance : TravelDistance, 0, Ms(380)) { EasingFunction = ease });
        glyph.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(260)));
    }
}
