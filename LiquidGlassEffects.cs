using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics.Effects;
using Windows.UI.Composition;
using WinRT;

namespace DynamicIsland;

static class LiquidGlassEffects
{
    static readonly Guid ClsidGaussianBlur = new("1feb6d69-2fe6-4ac9-8c58-1d7f93e7a6a5");
    static readonly Guid ClsidSaturation = new("5cb2d9cf-327d-459f-a0ce-40c0b2086bf7");
    static readonly Guid ClsidExposure = new("b56c8cfa-f634-41ee-bee0-ffa617106004");
    static readonly Guid ClsidAffineTransform = new("6aa97485-6354-4cfc-908c-e4a74f62c96c");
    static readonly Guid IidPropertyValue = new("4bd682dd-7554-40e9-9a9b-82654ede7e62");

    const uint MappingDirect = 1;

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString(string source, uint length, out IntPtr hstring);

    [ComImport, Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IInspectableAbi
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr iids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
    }

    [ComImport, Guid("2D8F9DDC-4339-4EB9-9216-F9DEB75658A2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsEffectSourceAbi
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr iids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
    }

    [ComImport, Guid("CB51C0CE-8FE6-4636-B202-861FAA07D8F3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsEffectAbi
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr iids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
        [PreserveSig] int get_Name(out IntPtr name);
        [PreserveSig] int put_Name(IntPtr name);
    }

    [ComImport, Guid("2FC57384-A068-44D7-A331-30982FCF7177"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsEffectD2D1Interop
    {
        [PreserveSig] int GetEffectId(out Guid id);
        [PreserveSig] int GetNamedPropertyMapping([MarshalAs(UnmanagedType.LPWStr)] string name, out uint index, out uint mapping);
        [PreserveSig] int GetPropertyCount(out uint count);
        [PreserveSig] int GetProperty(uint index, out IntPtr value);
        [PreserveSig] int GetSource(uint index, out IntPtr source);
        [PreserveSig] int GetSourceCount(out uint count);
    }

    sealed class EffectNode : IInspectableAbi, IGraphicsEffectSourceAbi, IGraphicsEffectAbi, IGraphicsEffectD2D1Interop
    {
        readonly Guid _id;
        readonly object[] _properties;
        readonly object[] _sources;
        readonly Dictionary<string, uint> _named;
        string _name;

        public EffectNode(Guid id, string name, object[] properties, object[] sources, Dictionary<string, uint>? named = null)
        {
            _id = id;
            _name = name;
            _properties = properties;
            _sources = sources;
            _named = named ?? [];
        }

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

        public int get_Name(out IntPtr name)
        {
            if (string.IsNullOrEmpty(_name))
            {
                name = IntPtr.Zero;
                return 0;
            }
            return WindowsCreateString(_name, (uint)_name.Length, out name);
        }

        public int put_Name(IntPtr name)
        {
            _name = "";
            return 0;
        }

        public int GetEffectId(out Guid id)
        {
            id = _id;
            return 0;
        }

        public int GetNamedPropertyMapping(string name, out uint index, out uint mapping)
        {
            if (_named.TryGetValue(name, out index))
            {
                mapping = MappingDirect;
                return 0;
            }
            index = 0;
            mapping = 0;
            return unchecked((int)0x80070057);
        }

        public int GetPropertyCount(out uint count)
        {
            count = (uint)_properties.Length;
            return 0;
        }

        public int GetProperty(uint index, out IntPtr value)
        {
            if (index >= _properties.Length)
            {
                value = IntPtr.Zero;
                return unchecked((int)0x80070057);
            }

            object prop = _properties[index] switch
            {
                float f => PropertyValue.CreateSingle(f),
                uint u => PropertyValue.CreateUInt32(u),
                float[] arr => PropertyValue.CreateSingleArray(arr),
                _ => throw new NotSupportedException(),
            };
            IntPtr unk = MarshalInspectable<object>.FromManaged(prop);
            try
            {
                Guid iid = IidPropertyValue;
                return Marshal.QueryInterface(unk, ref iid, out value);
            }
            finally
            {
                Marshal.Release(unk);
            }
        }

        public int GetSource(uint index, out IntPtr source)
        {
            if (index >= _sources.Length)
            {
                source = IntPtr.Zero;
                return unchecked((int)0x80070057);
            }

            source = _sources[index] switch
            {
                EffectNode node => Marshal.GetComInterfaceForObject<EffectNode, IGraphicsEffectSourceAbi>(node),
                IGraphicsEffectSource param => MarshalInterface<IGraphicsEffectSource>.FromManaged(param),
                _ => throw new NotSupportedException(),
            };
            return 0;
        }

        public int GetSourceCount(out uint count)
        {
            count = (uint)_sources.Length;
            return 0;
        }
    }

    public static CompositionEffectBrush CreateCoreBrush(Compositor compositor, CompositionBrush backdrop,
        float blurSigma, float saturation, float exposure)
    {
        var input = new CompositionEffectSourceParameter("backdrop");
        var blur = new EffectNode(ClsidGaussianBlur, "blur", [blurSigma, 1u, 1u], [input]);
        var sat = new EffectNode(ClsidSaturation, "sat", [saturation], [blur]);
        var exp = new EffectNode(ClsidExposure, "exp", [exposure], [sat]);

        CompositionEffectFactory factory = CreateFactory(compositor, exp, null);
        CompositionEffectBrush brush = factory.CreateBrush();
        brush.SetSourceParameter("backdrop", backdrop);
        factory.Dispose();
        return brush;
    }

    public static CompositionEffectBrush CreateRefractiveRimBrush(Compositor compositor, CompositionBrush backdrop,
        float blurSigma, float saturation, float exposure)
    {
        var input = new CompositionEffectSourceParameter("backdrop");
        float[] identity = [1f, 0f, 0f, 1f, 0f, 0f];
        var transform = new EffectNode(ClsidAffineTransform, "transform", [1u, 1u, identity, 1f], [input],
            new Dictionary<string, uint> { ["TransformMatrix"] = 2 });
        var blur = new EffectNode(ClsidGaussianBlur, "blur", [blurSigma, 1u, 1u], [transform]);
        var sat = new EffectNode(ClsidSaturation, "sat", [saturation], [blur]);
        var exp = new EffectNode(ClsidExposure, "exp", [exposure], [sat]);

        CompositionEffectFactory factory = CreateFactory(compositor, exp, ["transform.TransformMatrix"]);
        CompositionEffectBrush brush = factory.CreateBrush();
        brush.SetSourceParameter("backdrop", backdrop);
        factory.Dispose();
        return brush;
    }

    static CompositionEffectFactory CreateFactory(Compositor compositor, EffectNode root, IEnumerable<string>? animatable)
    {
        IntPtr abi = Marshal.GetComInterfaceForObject<EffectNode, IGraphicsEffectAbi>(root);
        try
        {
            IGraphicsEffect effect = MarshalInterface<IGraphicsEffect>.FromAbi(abi);
            return animatable != null ? compositor.CreateEffectFactory(effect, animatable) : compositor.CreateEffectFactory(effect);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }
}
