using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using static DynamicIsland.Motion;

namespace DynamicIsland;

sealed class ShelfTile : Grid
{
    public const double TileWidth = 64;
    const double FrameSize = 56, FrameRadius = 14;
    const double IconSize = 36;
    const double HoverScale = 1.06;

    static readonly Brush TileBack = CreateTileBack();
    static readonly Brush TileRim = CreateTileRim();

    readonly Grid _frame;
    readonly Grid _content = new();
    readonly ScaleTransform _frameScale = new(1, 1);
    readonly Border _cross;

    public ShelfTile(Shelf.Item item)
    {
        Item = item;
        Width = TileWidth;
        Background = Brushes.Transparent;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(FrameSize) });
        RowDefinitions.Add(new RowDefinition());
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new ScaleTransform(1, 1);

        var frameBox = new Rect(0, 0, FrameSize, FrameSize);
        Rect rimBox = Rect.Inflate(frameBox, -0.5, -0.5);

        _frame = new Grid
        {
            Width = FrameSize,
            Height = FrameSize,
            Clip = Squircle.Of(frameBox, FrameRadius),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _frameScale,
        };
        _frame.Children.Add(new Border { Background = TileBack });
        _frame.Children.Add(_content);
        _frame.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Squircle.Of(rimBox, FrameRadius - 0.5),
            Stroke = TileRim,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        });
        RenderOptions.SetBitmapScalingMode(_frame, BitmapScalingMode.HighQuality);
        Children.Add(_frame);

        var name = new TextBlock
        {
            Text = item.Name,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0),
        };
        SetRow(name, 1);
        Children.Add(name);

        _cross = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x3A, 0x3A, 0x3C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -6, -2, 0),
            Opacity = 0,
            Cursor = Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Direction = 270, Opacity = 0.5, Color = Colors.Black },
            Child = new Icon { Kind = Glyph.Cross, Width = 10, Height = 10 },
        };
        _cross.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _cross.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Removed?.Invoke(this);
        };
        Children.Add(_cross);

        Cursor = Cursors.Hand;
        MouseEnter += (_, _) => SetHovered(true);
        MouseLeave += (_, _) => SetHovered(false);
        ShowPicture();
    }

    public Shelf.Item Item { get; }

    public event Action<ShelfTile>? Removed;

    public void ShowPicture()
    {
        if (Item.Picture is not { } picture) return;
        FrameworkElement shown;
        if (Item.IsPhoto)
        {
            shown = new Border
            {
                Background = new ImageBrush(picture) { Stretch = Stretch.UniformToFill },
            };
        }
        else
        {
            var holder = new Grid();
            holder.Children.Add(new Image
            {
                Source = picture,
                Stretch = Stretch.Uniform,
                Width = IconSize,
                Height = IconSize,
                Margin = new Thickness(0, 0, 0, 4),
            });
            string ext = System.IO.Path.GetExtension(Item.Name).TrimStart('.').ToUpperInvariant();
            if (ext.Length is > 0 and <= 4)
            {
                Color tint = BadgeTint(ext);
                holder.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromArgb(0xD8, tint.R, tint.G, tint.B)),
                    Padding = new Thickness(4, 0.5, 4, 1),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 4, 4),
                    Child = new TextBlock
                    {
                        Text = ext,
                        FontSize = 8,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                    },
                });
            }
            shown = holder;
        }
        shown.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(220)));
        _content.Children.Clear();
        _content.Children.Add(shown);
    }

    void SetHovered(bool on)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _frameScale.AnimateScale(new DoubleAnimation(on ? HoverScale : 1, Ms(on ? 160 : 260)) { EasingFunction = ease });
        _cross.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, Ms(on ? 120 : 220)));
    }

    static Color BadgeTint(string ext) => ext switch
    {
        "PDF" => Color.FromRgb(0xD9, 0x36, 0x2F),
        "TXT" or "MD" or "LOG" => Color.FromRgb(0x48, 0x48, 0x4E),
        "ZIP" or "RAR" or "7Z" => Color.FromRgb(0xC7, 0x7A, 0x0A),
        "MP4" or "MOV" or "MKV" => Color.FromRgb(0x7D, 0x5A, 0xE6),
        "MP3" or "WAV" or "FLAC" => Color.FromRgb(0xE0, 0x35, 0x62),
        "DOC" or "DOCX" => Color.FromRgb(0x1A, 0x73, 0xE8),
        "XLS" or "XLSX" or "CSV" => Color.FromRgb(0x23, 0x9B, 0x4E),
        "CS" or "PY" or "JS" or "TS" or "JSON" => Color.FromRgb(0x0A, 0x84, 0xD0),
        _ => Color.FromRgb(0x4C, 0x4C, 0x52),
    };

    static Brush CreateTileBack()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF), 1));
        brush.Freeze();
        return brush;
    }

    static Brush CreateTileRim()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x32, 0xFF, 0xFF, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 1));
        brush.Freeze();
        return brush;
    }
}
