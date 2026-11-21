using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.Graphics;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace DynamicIsland;

sealed class Glass
{
    const double FlattenTolerance = 0.2;
    const float CoreBlurSigma = 16f;
    const float CoreSaturation = 1.65f;
    const float CoreExposure = -0.28f;
    const float BevelBlurSigma = 3.5f;
    const float BevelSaturation = 1.85f;
    const float BevelExposure = -0.22f;
    const float BevelLensScale = 1.055f;
    const float BevelOpacity = 0.55f;

    const int D2D1_FACTORY_TYPE_SINGLE_THREADED = 0;
    const int D2D1_FILL_MODE_ALTERNATE = 0;
    const int D2D1_FILL_MODE_WINDING = 1;
    const int D2D1_FIGURE_BEGIN_FILLED = 0;
    const int D2D1_FIGURE_END_CLOSED = 1;
    const int CreatePathGeometrySlot = 10;
    const int PathOpenSlot = 17;
    const int SinkSetFillModeSlot = 3;
    const int SinkBeginFigureSlot = 5;
    const int SinkAddLinesSlot = 6;
    const int SinkEndFigureSlot = 8;
    const int SinkCloseSlot = 9;
    static readonly Guid FactoryId = new("06152247-6F50-465A-9245-118BFD3B6007");

    [ComImport, Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IInspectableAbi
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr iids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
    }

    [ComImport, Guid("CAFF7902-670C-4181-A624-DA977203B845"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGeometrySource2DAbi
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr iids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
    }

    [ComImport, Guid("0657AF73-53FD-47CF-84FF-C8492D2A80A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGeometrySource2DInterop
    {
        [PreserveSig] int GetGeometry(out IntPtr value);
        [PreserveSig] int TryGetGeometryUsingFactory(IntPtr factory, out IntPtr value);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Point2F { public float X, Y; }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreatePathGeometryCall(IntPtr factory, out IntPtr geometry);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int PathOpenCall(IntPtr geometry, out IntPtr sink);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void SetFillModeCall(IntPtr sink, int mode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void BeginFigureCall(IntPtr sink, Point2F start, int begin);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void AddLinesCall(IntPtr sink, IntPtr points, uint count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void EndFigureCall(IntPtr sink, int end);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CloseSinkCall(IntPtr sink);

    [DllImport("d2d1.dll")]
    static extern int D2D1CreateFactory(int factoryType, ref Guid riid, IntPtr options, out IntPtr factory);

    sealed class PathSource(List<Point2F[]> figures, int fillMode, IntPtr fallbackFactory) : IInspectableAbi, IGeometrySource2DAbi, IGeometrySource2DInterop
    {
        public int GetIids(out uint count, out IntPtr iids)
        {
            count = 0;
            iids = IntPtr.Zero;
            return 0;
        }

        public int GetRuntimeClassName(out IntPtr name)
        {
            name = IntPtr.Zero;
            return 0;
        }

        public int GetTrustLevel(out int level)
        {
            level = 0;
            return 0;
        }

        public int GetGeometry(out IntPtr value)
        {
            value = fallbackFactory != IntPtr.Zero ? BuildD2DPath(fallbackFactory, figures, fillMode) : IntPtr.Zero;
            return value != IntPtr.Zero ? 0 : unchecked((int)0x80004005);
        }

        public int TryGetGeometryUsingFactory(IntPtr factory, out IntPtr value)
        {
            value = factory != IntPtr.Zero ? BuildD2DPath(factory, figures, fillMode) : IntPtr.Zero;
            return value != IntPtr.Zero ? 0 : unchecked((int)0x80004001);
        }
    }

    sealed class Pane
    {
        public readonly SpriteVisual Visual;
        readonly CompositionRoundedRectangleGeometry _outline;

        public Pane(Compositor compositor, CompositionBrush backdrop)
        {
            _outline = compositor.CreateRoundedRectangleGeometry();
            Visual = compositor.CreateSpriteVisual();
            Visual.RelativeSizeAdjustment = Vector2.One;
            Visual.Brush = backdrop;
            Visual.Clip = compositor.CreateGeometricClip(_outline);
            Visual.IsVisible = false;
        }

        public void Fit((Rect Box, double Radius) part, Matrix toPixels)
        {
            Visual.IsVisible = !part.Box.IsEmpty;
            if (part.Box.IsEmpty) return;
            Point from = toPixels.Transform(part.Box.TopLeft), to = toPixels.Transform(part.Box.BottomRight);
            _outline.Offset = new Vector2((float)from.X, (float)from.Y);
            _outline.Size = new Vector2((float)(to.X - from.X), (float)(to.Y - from.Y));
            _outline.CornerRadius = new Vector2((float)(part.Radius * toPixels.M11), (float)(part.Radius * toPixels.M22));
        }
    }

    readonly Window _over;
    Compositor? _compositor;
    DesktopWindowTarget? _target;
    IntPtr _hwnd, _d2dFactory;
    SpriteVisual? _shapeVisual, _bevelVisual;
    CompositionPathGeometry? _shapeOutline, _bevelOutline;
    CompositionEffectBrush? _bevelBrush;
    bool _bevelHasTransform;
    Pane? _pill, _bubble;
    bool _pathSupported = true;

    public Glass(Window over) => _over = over;

    public bool IsOn => _target != null;

    public IntPtr Handle => _hwnd;

    public void Show()
    {
        if (_target != null) return;
        try
        {
            Native.EnsureDispatcherQueue();
            _hwnd = Native.CreateBackdropWindow();
            _compositor = new Compositor();
            _target = DesktopComposition.TargetFor(_compositor, _hwnd);

            CompositionBrush rawBackdrop = _compositor.CreateHostBackdropBrush();
            CompositionBrush coreBrush = TryCreateCoreBrush(_compositor, rawBackdrop);
            CompositionBrush? bevelBrush = TryCreateBevelBrush(_compositor, rawBackdrop);

            Windows.UI.Composition.ContainerVisual root = _compositor.CreateContainerVisual();
            root.RelativeSizeAdjustment = Vector2.One;

            InitD2D();
            if (_pathSupported)
            {
                _shapeOutline = _compositor.CreatePathGeometry();
                _shapeVisual = _compositor.CreateSpriteVisual();
                _shapeVisual.RelativeSizeAdjustment = Vector2.One;
                _shapeVisual.Brush = coreBrush;
                _shapeVisual.Clip = _compositor.CreateGeometricClip(_shapeOutline);
                _shapeVisual.IsVisible = false;
                root.Children.InsertAtTop(_shapeVisual);

                if (bevelBrush != null)
                {
                    _bevelOutline = _compositor.CreatePathGeometry();
                    _bevelVisual = _compositor.CreateSpriteVisual();
                    _bevelVisual.RelativeSizeAdjustment = Vector2.One;
                    _bevelVisual.Brush = bevelBrush;
                    _bevelVisual.Opacity = BevelOpacity;
                    _bevelVisual.Clip = _compositor.CreateGeometricClip(_bevelOutline);
                    _bevelVisual.IsVisible = false;
                    root.Children.InsertAtTop(_bevelVisual);
                }
            }

            _pill = new Pane(_compositor, coreBrush);
            _bubble = new Pane(_compositor, coreBrush);
            root.Children.InsertAtTop(_pill.Visual);
            root.Children.InsertAtTop(_bubble.Visual);
            _target.Root = root;
            Place();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            Hide();
        }
    }

    CompositionBrush TryCreateCoreBrush(Compositor compositor, CompositionBrush rawBackdrop)
    {
        try
        {
            return LiquidGlassEffects.CreateCoreBrush(compositor, rawBackdrop, CoreBlurSigma, CoreSaturation, CoreExposure);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return rawBackdrop;
        }
    }

    CompositionBrush? TryCreateBevelBrush(Compositor compositor, CompositionBrush rawBackdrop)
    {
        try
        {
            _bevelBrush = LiquidGlassEffects.CreateRefractiveRimBrush(compositor, rawBackdrop, BevelBlurSigma, BevelSaturation, BevelExposure);
            _bevelHasTransform = true;
            return _bevelBrush;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            _bevelHasTransform = false;
            try
            {
                _bevelBrush = LiquidGlassEffects.CreateCoreBrush(compositor, rawBackdrop, BevelBlurSigma, BevelSaturation, BevelExposure);
                return _bevelBrush;
            }
            catch
            {
                return null;
            }
        }
    }

    public void Hide()
    {
        _bevelBrush?.Dispose();
        _bevelOutline?.Dispose();
        _bevelVisual?.Dispose();
        _shapeOutline?.Dispose();
        _shapeVisual?.Dispose();
        _target?.Dispose();
        _compositor?.Dispose();
        if (_d2dFactory != IntPtr.Zero) Marshal.Release(_d2dFactory);
        if (_hwnd != IntPtr.Zero) Native.Destroy(_hwnd);
        _bevelBrush = null;
        _bevelHasTransform = false;
        _bevelOutline = null;
        _bevelVisual = null;
        _shapeOutline = null;
        _shapeVisual = null;
        _target = null;
        _compositor = null;
        _d2dFactory = IntPtr.Zero;
        _hwnd = IntPtr.Zero;
        _pill = _bubble = null;
    }

    public void Place()
    {
        if (_hwnd != IntPtr.Zero) Native.PlaceBehind(_hwnd, new WindowInteropHelper(_over).Handle);
    }

    public void Cut(Geometry? shape, Geometry? bevelRing, (Rect Box, double Radius) pill, (Rect Box, double Radius) bubble, Matrix toPixels)
    {
        if (_pathSupported && _shapeVisual != null && _shapeOutline != null)
        {
            if (TryCutPath(shape, bevelRing, pill.Box, toPixels))
            {
                if (_pill != null) _pill.Visual.IsVisible = false;
                if (_bubble != null) _bubble.Visual.IsVisible = false;
                return;
            }
            _pathSupported = false;
            _shapeVisual.IsVisible = false;
            if (_bevelVisual != null) _bevelVisual.IsVisible = false;
        }

        _pill?.Fit(pill, toPixels);
        _bubble?.Fit(bubble, toPixels);
    }

    void InitD2D()
    {
        if (_d2dFactory != IntPtr.Zero) return;
        Guid id = FactoryId;
        if (D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, ref id, IntPtr.Zero, out _d2dFactory) < 0)
            _d2dFactory = IntPtr.Zero;
    }

    bool TryCutPath(Geometry? shape, Geometry? bevelRing, Rect pillBox, Matrix toPixels)
    {
        if (shape == null || shape.IsEmpty())
        {
            _shapeVisual!.IsVisible = false;
            if (_bevelVisual != null) _bevelVisual.IsVisible = false;
            return true;
        }

        List<Point2F[]> figures = Flatten(shape, toPixels);
        if (figures.Count == 0)
        {
            _shapeVisual!.IsVisible = false;
            if (_bevelVisual != null) _bevelVisual.IsVisible = false;
            return true;
        }

        try
        {
            SetOutline(_shapeOutline!, figures, D2D1_FILL_MODE_WINDING);
            _shapeVisual!.IsVisible = true;

            if (_bevelVisual != null && _bevelOutline != null && bevelRing != null && !bevelRing.IsEmpty())
            {
                List<Point2F[]> ringFigures = Flatten(bevelRing, toPixels);
                if (ringFigures.Count > 0)
                {
                    if (_bevelHasTransform && _bevelBrush != null && !pillBox.IsEmpty)
                    {
                        Point center = toPixels.Transform(new Point(pillBox.X + pillBox.Width / 2, pillBox.Y + pillBox.Height / 2));
                        _bevelBrush.Properties.InsertMatrix3x2("transform.TransformMatrix",
                            Matrix3x2.CreateScale(BevelLensScale, new Vector2((float)center.X, (float)center.Y)));
                    }
                    SetOutline(_bevelOutline, ringFigures, D2D1_FILL_MODE_ALTERNATE);
                    _bevelVisual.IsVisible = true;
                }
                else
                {
                    _bevelVisual.IsVisible = false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return false;
        }
    }

    void SetOutline(CompositionPathGeometry outline, List<Point2F[]> figures, int fillMode)
    {
        var source = new PathSource(figures, fillMode, _d2dFactory);
        IntPtr abi = Marshal.GetComInterfaceForObject<PathSource, IGeometrySource2DAbi>(source);
        try
        {
            IGeometrySource2D wrapped = MarshalInterface<IGeometrySource2D>.FromAbi(abi);
            outline.Path = new CompositionPath(wrapped);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    static List<Point2F[]> Flatten(Geometry shape, Matrix toPixels)
    {
        PathGeometry flat = shape.GetFlattenedPathGeometry(FlattenTolerance, ToleranceType.Absolute);
        var figures = new List<Point2F[]>(flat.Figures.Count);
        foreach (PathFigure figure in flat.Figures)
        {
            var pts = new List<Point2F>(64);
            foreach (Point pt in figure.Points())
            {
                Point px = toPixels.Transform(pt);
                pts.Add(new Point2F { X = (float)px.X, Y = (float)px.Y });
            }
            if (pts.Count >= 3) figures.Add([.. pts]);
        }
        return figures;
    }

    static IntPtr BuildD2DPath(IntPtr factory, List<Point2F[]> figures, int fillMode)
    {
        var createPath = Marshal.GetDelegateForFunctionPointer<CreatePathGeometryCall>(Slot(factory, CreatePathGeometrySlot));
        if (createPath(factory, out IntPtr geometry) < 0 || geometry == IntPtr.Zero) return IntPtr.Zero;
        IntPtr sink = IntPtr.Zero;
        try
        {
            var open = Marshal.GetDelegateForFunctionPointer<PathOpenCall>(Slot(geometry, PathOpenSlot));
            if (open(geometry, out sink) < 0 || sink == IntPtr.Zero)
            {
                Marshal.Release(geometry);
                return IntPtr.Zero;
            }

            var setFill = Marshal.GetDelegateForFunctionPointer<SetFillModeCall>(Slot(sink, SinkSetFillModeSlot));
            var begin = Marshal.GetDelegateForFunctionPointer<BeginFigureCall>(Slot(sink, SinkBeginFigureSlot));
            var addLines = Marshal.GetDelegateForFunctionPointer<AddLinesCall>(Slot(sink, SinkAddLinesSlot));
            var end = Marshal.GetDelegateForFunctionPointer<EndFigureCall>(Slot(sink, SinkEndFigureSlot));
            var close = Marshal.GetDelegateForFunctionPointer<CloseSinkCall>(Slot(sink, SinkCloseSlot));

            setFill(sink, fillMode);
            foreach (Point2F[] pts in figures)
            {
                begin(sink, pts[0], D2D1_FIGURE_BEGIN_FILLED);
                GCHandle pinned = GCHandle.Alloc(pts, GCHandleType.Pinned);
                try
                {
                    IntPtr second = IntPtr.Add(pinned.AddrOfPinnedObject(), Marshal.SizeOf<Point2F>());
                    addLines(sink, second, (uint)(pts.Length - 1));
                }
                finally
                {
                    pinned.Free();
                }
                end(sink, D2D1_FIGURE_END_CLOSED);
            }

            if (close(sink) < 0)
            {
                Marshal.Release(geometry);
                return IntPtr.Zero;
            }
            return geometry;
        }
        catch
        {
            Marshal.Release(geometry);
            return IntPtr.Zero;
        }
        finally
        {
            if (sink != IntPtr.Zero) Marshal.Release(sink);
        }
    }

    static IntPtr Slot(IntPtr instance, int index) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size);
}
