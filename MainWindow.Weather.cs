using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland;

public partial class MainWindow
{
    const double WeatherPagePadding = 14;
    const double PlacePagePadding = 14;
    const double PlaceCheckSize = 14;
    const int ShortestQuery = 2;
    const double HourIconSize = 20;
    const string LocationSettings = "ms-settings:privacy-location";
    const string Minus = "−";
    static readonly TimeSpan SearchPause = TimeSpan.FromMilliseconds(350);

    readonly WeatherService _weather = new();
    readonly List<(TextBlock At, Icon Sky, TextBlock Degrees)> _weatherHours = [];
    readonly DelayedAction _placeSearch;
    CancellationTokenSource? _searching;
    Place[] _found = [];
    string _foundFor = "";
    IntPtr _typedAfter;

    bool IsTyping => _view == View.Place && IsActive;

    void PrepareWeatherHours()
    {
        for (int i = 0; i < WeatherService.ShownHours; i++)
        {
            var at = new TextBlock { FontSize = 11, Foreground = _dim, HorizontalAlignment = HorizontalAlignment.Center };
            var sky = new Icon { Width = HourIconSize, Height = HourIconSize, Margin = new Thickness(0, 6, 0, 5) };
            var degrees = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            WeatherHours.Children.Add(new StackPanel { Children = { at, sky, degrees } });
            _weatherHours.Add((at, sky, degrees));
        }
    }

    void SyncWeather()
    {
        WeatherService.Report? now = _weather.Now;
        InfoWeatherRow.SetVisible(now != null);
        WeatherNow.SetVisible(now != null);
        WeatherHours.SetVisible(now != null);
        WeatherSpark.SetVisible(now != null && now.Hours.Length >= 2);
        WeatherGlow.SetVisible(now != null);
        WeatherNote.SetVisible(now == null);
        MenuWeather.Text = now != null ? DescribeDegrees(now.Degrees) : "";
        PlaceText.Text = Settings.Place?.Name ?? "Автоматически";
        if (now != null) ShowWeather(now);
        else ShowWeatherNote();
        SyncRim();
        FitWeatherPage();
    }

    static Color WeatherColor(Sky sky, bool night) => sky switch
    {
        Sky.Clear => night ? Color.FromRgb(0x7D, 0x7A, 0xFF) : Color.FromRgb(0xFF, 0x9F, 0x0A),
        Sky.Cloudy => night ? Color.FromRgb(0x8E, 0x98, 0xFF) : Color.FromRgb(0xFF, 0xB3, 0x40),
        Sky.Rain or Sky.Snow => Color.FromRgb(0x64, 0xD2, 0xFF),
        Sky.Storm => Color.FromRgb(0xFF, 0xD6, 0x0A),
        _ => Color.FromRgb(0xB8, 0xC6, 0xDA),
    };

    void ShowWeather(WeatherService.Report now)
    {
        Glyph glyph = GlyphOf(now.Sky, now.Night);
        Color tint = WeatherColor(now.Sky, now.Night);
        Brush tintBrush = Shades.Frozen(new SolidColorBrush(tint));
        InfoWeatherIcon.Kind = MenuWeatherIcon.Kind = WeatherIcon.Kind = glyph;
        WeatherIcon.Fill = InfoWeatherIcon.Fill = tintBrush;

        var glow = new RadialGradientBrush(tint.WithAlpha(0.24), tint.WithAlpha(0))
        {
            Center = new Point(0.22, 0.15),
            GradientOrigin = new Point(0.22, 0.15),
            RadiusX = 0.72,
            RadiusY = 0.65,
        };
        glow.Freeze();
        WeatherGlow.Background = glow;

        InfoWeather.Text = WeatherDegrees.Text = DescribeDegrees(now.Degrees);
        WeatherSky.Text = now.Sky switch
        {
            Sky.Clear => "Ясно",
            Sky.Cloudy => "Облачно",
            Sky.Overcast => "Пасмурно",
            Sky.Fog => "Туман",
            Sky.Rain => "Дождь",
            Sky.Snow => "Снег",
            _ => "Гроза",
        };
        WeatherFeels.Text = "Ощущается как " + DescribeDegrees(now.Feels);
        WeatherWind.Text = $"Ветер {now.Wind} м/с";

        for (int i = 0; i < _weatherHours.Count; i++)
        {
            (TextBlock at, Icon sky, TextBlock degrees) = _weatherHours[i];
            bool known = i < now.Hours.Length;
            ((UIElement)at.Parent).Visibility = known ? Visibility.Visible : Visibility.Hidden;
            if (!known) continue;

            WeatherService.Hour hour = now.Hours[i];
            at.Text = hour.At.ToString("00") + ":00";
            sky.Kind = GlyphOf(hour.Sky, hour.Night);
            sky.Fill = Shades.Frozen(new SolidColorBrush(WeatherColor(hour.Sky, hour.Night)));
            degrees.Text = DescribeDegrees(hour.Degrees);
        }
        WeatherSpark.SetData([.. now.Hours.Select(h => h.Degrees)], tint);
    }

    void ShowWeatherNote()
    {
        (string title, string text, string action) = _weather.State switch
        {
            WeatherService.Stage.Off => ("Погода выключена", "Остров не узнаёт, где вы, и ничего не запрашивает", "Включить"),
            WeatherService.Stage.NoPlace => ("Нет доступа к месту", "Включите в Windows расположение и доступ к нему для классических приложений или выберите город", "Открыть параметры"),
            WeatherService.Stage.Failed => ("Не получилось", "Остров попробует ещё раз сам", "Попробовать сейчас"),
            _ => ("Узнаю погоду…", "", ""),
        };
        WeatherNoteTitle.Text = title;
        WeatherNoteText.Text = text;
        WeatherNoteText.SetVisible(text.Length > 0);
        WeatherAction.Content = action;
        WeatherAction.SetVisible(action.Length > 0);
    }

    void FitWeatherPage()
    {
        WeatherBody.UpdateLayout();
        WeatherBody.Measure(new Size(WeatherView.Width, double.PositiveInfinity));
        double height = Math.Ceiling(WeatherBody.DesiredSize.Height) + WeatherPagePadding;
        if (height == WeatherView.Height) return;

        WeatherView.Height = height;
        if (_view == View.Weather) UpdateTargets();
    }

    static string DescribeDegrees(int degrees) => (degrees > 0 ? "+" : degrees < 0 ? Minus : "") + Math.Abs(degrees) + "°";

    static Glyph GlyphOf(Sky sky, bool night) => sky switch
    {
        Sky.Clear => night ? Glyph.Moon : Glyph.Sun,
        Sky.Cloudy => night ? Glyph.CloudMoon : Glyph.CloudSun,
        Sky.Overcast => Glyph.Cloud,
        Sky.Fog => Glyph.Fog,
        Sky.Rain => Glyph.Rain,
        Sky.Snow => Glyph.Snow,
        _ => Glyph.Storm,
    };

    void WeatherRow_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(Panel.Weather);
        _ = _weather.RefreshAsync(_weather.State != WeatherService.Stage.Ready);
    }

    void WeatherAction_Click(object sender, RoutedEventArgs e)
    {
        if (_weather.State == WeatherService.Stage.Off) SetWeather(true);
        else if (_weather.State == WeatherService.Stage.NoPlace) OpenLocationSettings();
        _ = _weather.RefreshAsync(true);
    }

    void Weather_Click(object sender, RoutedEventArgs e)
    {
        SetWeather(!Settings.Weather);
        _ = _weather.RefreshAsync(true);
    }

    void SetWeather(bool on)
    {
        Settings.Weather = on;
        UpdateSwitches(true);
    }

    void PlaceRow_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Place);

    void PlaceBack_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Weather);

    void PlaceAuto_Click(object sender, RoutedEventArgs e) => ChoosePlace(null);

    void PlaceResult_Click(object sender, RoutedEventArgs e) => ChoosePlace((Place)((Button)sender).Tag);

    void ChoosePlace(Place? place)
    {
        bool moved = place != Settings.Place;
        Settings.Place = place;
        if (moved) _weather.Relocate();
        ShowPanel(Panel.Weather);
    }

    void BeginTyping()
    {
        PlaceQuery.Clear();
        ShowPlaces();
        IntPtr foreground = Native.ForegroundWindow;
        if (foreground != _hwnd) _typedAfter = foreground;
        Activate();
        Dispatcher.BeginInvoke(() => PlaceQuery.Focus(), DispatcherPriority.Input);
    }

    void EndTyping()
    {
        _placeSearch.Cancel();
        _searching?.Cancel();
        if (IsActive && _typedAfter != IntPtr.Zero) Native.BringForward(_typedAfter);
        _typedAfter = IntPtr.Zero;
    }

    void OnDeactivated(object? sender, EventArgs e)
    {
        if (_panel == Panel.Place && !_hovered) _collapseTimeout.Start(CollapseDelay);
    }

    void PlaceField_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Activate();
        PlaceQuery.Focus();
        PlaceQuery.CaretIndex = PlaceQuery.Text.Length;
        e.Handled = true;
    }

    void PlaceQuery_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) ShowPanel(Panel.Weather);
        else if (e.Key == Key.Enter && _found.Length > 0 && _foundFor == PlaceQuery.Text.Trim()) ChoosePlace(_found[0]);
        else return;
        e.Handled = true;
    }

    void PlaceQuery_TextChanged(object sender, TextChangedEventArgs e)
    {
        PlaceHint.SetVisible(PlaceQuery.Text.Length == 0);
        _searching?.Cancel();
        bool enough = PlaceQuery.Text.Trim().Length >= ShortestQuery;
        if (enough) _placeSearch.Start(SearchPause);
        else
        {
            _placeSearch.Cancel();
            _found = [];
        }
        PlaceStatus.Text = enough && _found.Length == 0 ? "Ищу…" : "";
        ShowPlaces();
    }

    async void SearchPlaces()
    {
        var searching = _searching = new CancellationTokenSource();
        try
        {
            string query = PlaceQuery.Text.Trim();
            _found = await WeatherService.SearchAsync(query, searching.Token);
            _foundFor = query;
            PlaceStatus.Text = _found.Length == 0 ? "Ничего не нашлось" : "";
        }
        catch (OperationCanceledException) when (searching.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            if (ex is not (HttpRequestException or TaskCanceledException)) App.Log(ex);
            _found = [];
            PlaceStatus.Text = "Не получилось найти";
        }
        ShowPlaces();
    }

    void ShowPlaces()
    {
        Place? chosen = Settings.Place;
        bool searching = PlaceQuery.Text.Trim().Length >= ShortestQuery;
        Place[] shown = searching ? _found : chosen != null ? [chosen] : [];

        PlaceAutoCheck.SetVisible(chosen == null);
        PlaceResults.Children.Clear();
        foreach (Place place in shown) PlaceResults.Children.Add(PlaceRowOf(place, place == chosen));
        PlaceResults.SetVisible(shown.Length > 0);
        PlaceStatus.SetVisible(PlaceStatus.Text.Length > 0);
        FitPlacePage();
    }

    Button PlaceRowOf(Place place, bool chosen)
    {
        var name = new TextBlock { Text = place.Name, VerticalAlignment = VerticalAlignment.Center };
        var region = new TextBlock
        {
            Text = place.Region,
            FontSize = 12,
            Foreground = _dim,
            Margin = new Thickness(8, 1, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var check = new Icon { Kind = Glyph.Check, Width = PlaceCheckSize, Height = PlaceCheckSize, Fill = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        check.SetVisible(chosen);

        var row = new Grid { ColumnDefinitions = { new() { Width = GridLength.Auto }, new(), new() { Width = GridLength.Auto } } };
        Grid.SetColumn(region, 1);
        Grid.SetColumn(check, 2);
        row.Children.Add(name);
        row.Children.Add(region);
        row.Children.Add(check);

        var button = new Button { Style = (Style)FindResource("RowButton"), Content = row, Tag = place };
        button.Click += PlaceResult_Click;
        return button;
    }

    void FitPlacePage()
    {
        PlaceBody.Measure(new Size(PlaceView.Width, double.PositiveInfinity));
        double height = Math.Ceiling(PlaceBody.DesiredSize.Height) + PlacePagePadding;
        if (height == PlaceView.Height) return;

        PlaceView.Height = height;
        if (_view == View.Place) UpdateTargets();
    }

    static void OpenLocationSettings()
    {
        try { Process.Start(new ProcessStartInfo(LocationSettings) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { App.Log(ex); }
    }
}
