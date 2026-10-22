using System.Globalization;
using System.Windows.Media;
using Microsoft.Win32;

namespace DynamicIsland;

[Flags]
enum Backdrop
{
    Glow = 0,
    Matrix = 1,
    Stars = 2,
    MatrixAndStars = Matrix | Stars,
}

enum LyricChange
{
    Smooth,
    Wave,
    Drum,
}

[Flags]
enum LyricSource
{
    Lrclib = 1,
    Deezer = 2,
    NetEase = 4,
    QqMusic = 8,
    Kugou = 16,
    Paxsenix = 32,
    Usual = Lrclib | Deezer | NetEase | QqMusic | Kugou,
    All = Usual | Paxsenix,
}

enum PointerLyricShape
{
    Column,
    Line,
}

enum Hover
{
    Disc,
    Magnet,
    Flow,
}

enum SeekHover
{
    Magnifier,
    Lift,
    Wave,
}

static class Settings
{
    const string Key = @"Software\DynamicIsland";
    const int MinScale = 85, MaxScale = 130, MaxGap = 24;
    const int DefaultScale = 100, DefaultGap = 8;

    static bool _lyrics = ReadSwitch(nameof(Lyrics)), _lyricEffects = ReadSwitch(nameof(LyricEffects));
    static bool _network = ReadSwitch(nameof(Network)), _hideFullscreen = ReadSwitch(nameof(HideFullscreen));
    static bool _rim = ReadSwitch(nameof(Rim)), _appVolume = ReadSwitch(nameof(AppVolume));
    static bool _appSpectrum = ReadSwitch(nameof(AppSpectrum));
    static bool _weather = ReadSwitch(nameof(Weather));
    static bool _pointerLyric = ReadSwitch(nameof(PointerLyric), false);
    static bool _lyricParting = ReadSwitch(nameof(LyricParting));
    static PointerLyricShape _pointerLyricShape = (PointerLyricShape)Math.Clamp(Read(nameof(PointerLyricShape), 0), 0, (int)PointerLyricShape.Line);
    static Place? _place = ReadPlace();
    static bool _dots = ReadSwitch(nameof(Dots), false);
    static bool _lineBar = ReadSwitch(nameof(LineBar));
    static bool _glass = ReadSwitch(nameof(Glass), false);
    static Backdrop _backdrop = (Backdrop)Math.Clamp(Read(nameof(Backdrop), 0), 0, (int)Backdrop.MatrixAndStars);
    static LyricChange _lyricChange = (LyricChange)Math.Clamp(Read(nameof(LyricChange), (int)LyricChange.Wave), 0, (int)LyricChange.Drum);
    static LyricSource _lyricSources = Known((LyricSource)Read(nameof(LyricSources), (int)LyricSource.Usual));
    static Hover _hover = (Hover)Math.Clamp(Read(nameof(Hover), 0), 0, (int)Hover.Flow);
    static SeekHover _seekHover = (SeekHover)Math.Clamp(Read(nameof(SeekHover), 0), 0, (int)SeekHover.Wave);
    static int _scale = Math.Clamp(Read(nameof(Scale), DefaultScale), MinScale, MaxScale);
    static int _gap = Math.Clamp(Read(nameof(Gap), DefaultGap), 0, MaxGap);
    static int _accent = Read(nameof(Accent), 0);

    public static bool Dots
    {
        get => _dots;
        set => Write(nameof(Dots), _dots = value);
    }

    public static bool LineBar
    {
        get => _lineBar;
        set => Write(nameof(LineBar), _lineBar = value);
    }

    public static bool Glass
    {
        get => _glass;
        set => Write(nameof(Glass), _glass = value);
    }

    public static Backdrop Backdrop
    {
        get => _backdrop;
        set => Write(nameof(Backdrop), (int)(_backdrop = value));
    }

    public static LyricChange LyricChange
    {
        get => _lyricChange;
        set => Write(nameof(LyricChange), (int)(_lyricChange = value));
    }

    public static LyricSource LyricSources
    {
        get => _lyricSources;
        set => Write(nameof(LyricSources), (int)(_lyricSources = Known(value)));
    }

    public static Hover Hover
    {
        get => _hover;
        set => Write(nameof(Hover), (int)(_hover = value));
    }

    public static SeekHover SeekHover
    {
        get => _seekHover;
        set => Write(nameof(SeekHover), (int)(_seekHover = value));
    }

    public static bool Lyrics
    {
        get => _lyrics;
        set => Write(nameof(Lyrics), _lyrics = value);
    }

    public static bool LyricEffects
    {
        get => _lyricEffects;
        set => Write(nameof(LyricEffects), _lyricEffects = value);
    }

    public static bool PointerLyric
    {
        get => _pointerLyric;
        set => Write(nameof(PointerLyric), _pointerLyric = value);
    }

    public static PointerLyricShape PointerLyricShape
    {
        get => _pointerLyricShape;
        set => Write(nameof(PointerLyricShape), (int)(_pointerLyricShape = value));
    }

    public static bool LyricParting
    {
        get => _lyricParting;
        set => Write(nameof(LyricParting), _lyricParting = value);
    }

    public static bool Rim
    {
        get => _rim;
        set => Write(nameof(Rim), _rim = value);
    }

    public static bool AppVolume
    {
        get => _appVolume;
        set => Write(nameof(AppVolume), _appVolume = value);
    }

    public static bool AppSpectrum
    {
        get => _appSpectrum;
        set => Write(nameof(AppSpectrum), _appSpectrum = value);
    }

    /// <summary>Notices about Wi-Fi, Ethernet and VPN.</summary>
    public static bool Network
    {
        get => _network;
        set => Write(nameof(Network), _network = value);
    }

    public static bool Weather
    {
        get => _weather;
        set => Write(nameof(Weather), _weather = value);
    }

    public static Place? Place
    {
        get => _place;
        set
        {
            _place = value;
            string[] stored = value is { } place ? [place.Name, place.Region, Coordinate(place.Latitude), Coordinate(place.Longitude)] : [];
            Write(nameof(Place), stored, RegistryValueKind.MultiString);
        }
    }

    public static bool HideFullscreen
    {
        get => _hideFullscreen;
        set => Write(nameof(HideFullscreen), _hideFullscreen = value);
    }

    public static int Scale
    {
        get => _scale;
        set => Write(nameof(Scale), _scale = Math.Clamp(value, MinScale, MaxScale));
    }

    public static int Gap
    {
        get => _gap;
        set => Write(nameof(Gap), _gap = Math.Clamp(value, 0, MaxGap));
    }

    public static Color? Accent
    {
        get => _accent == 0 ? null : Color.FromRgb((byte)(_accent >> 16), (byte)(_accent >> 8), (byte)_accent);
        set => Write(nameof(Accent), _accent = value is { } c ? c.R << 16 | c.G << 8 | c.B : 0);
    }

    public static string[] Shelf
    {
        get => Read<string[]>(nameof(Shelf), []);
        set => Write(nameof(Shelf), value, RegistryValueKind.MultiString);
    }

    static LyricSource Known(LyricSource sources) => (sources & LyricSource.All) == 0 ? LyricSource.Usual : sources & LyricSource.All;

    static Place? ReadPlace() =>
        Read<string[]>(nameof(Place), []) is [var name, var region, var latitude, var longitude]
        && double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
        && double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)
            ? new Place(name, region, lat, lon) : null;

    static string Coordinate(double degrees) => degrees.ToString("R", CultureInfo.InvariantCulture);

    static bool ReadSwitch(string name, bool fallback = true) => Read(name, fallback ? 1 : 0) != 0;

    static T Read<T>(string name, T fallback)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(name) is T value ? value : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    static void Write(string name, bool value) => Write(name, value ? 1 : 0);

    static void Write(string name, int value) => Write(name, value, RegistryValueKind.DWord);

    static void Write(string name, object value, RegistryValueKind kind)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue(name, value, kind);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}
