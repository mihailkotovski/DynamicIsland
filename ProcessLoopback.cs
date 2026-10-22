using System.Runtime.InteropServices;

namespace DynamicIsland;

/// <summary>
/// Capture of a single process, through the virtual device VAD\Process_Loopback.
///
/// The audio engine calls the completion handler from a thread of its own, so the handler is a COM
/// object built by hand: its vtable sits in unmanaged memory and its QueryInterface answers
/// IID_IAgileObject, which is how the engine decides the callback may be called without marshalling.
/// Everything else goes through vtable slots too, so no runtime COM object is ever created.
/// </summary>
sealed class ProcessLoopback : IDisposable
{
    const string Device = @"VAD\Process_Loopback";

    // vtable slots, counting IUnknown's three first
    const int OpGetActivateResult = 3;
    const int ClientInitialize = 3, ClientGetService = 14, ClientStart = 10, ClientStop = 11;
    const int CaptureGetBuffer = 3, CaptureReleaseBuffer = 4, CaptureGetNextPacketSize = 5;

    const int ShareShared = 0, FormatPcm = 1;
    const int StreamLoopback = 0x00020000;
    const int AutoConvertPcm = 0x08000000;
    const int SourceDefaultQuality = unchecked((int)0x80000000);
    const int ActivationProcessLoopback = 1, LoopbackIncludeTargetTree = 0;
    const int VtBlob = 65, Ok = 0, NoInterface = unchecked((int)0x80004002);
    const int Rate = 48000, Channels = 2, Bits = 16;
    const long BufferDuration = 200_000; // 20 ms, in 100 ns units

    static readonly Guid GuidAgile = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");
    static readonly Guid GuidHandler = new("72A22D78-CDE4-431D-B8D0-26AEB6FF8E9D");
    static readonly Guid GuidUnknown = new("00000000-0000-0000-C000-000000000046");
    static readonly Guid GuidAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    static readonly Guid GuidCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    IntPtr _self, _iid, _body, _props, _client, _capture;
    Callback? _callback;

    public ProcessLoopback(uint pid)
    {
        _callback = new Callback();
        _self = _callback.Self;

        var wanted = new ActivationParams
        {
            ActivationType = ActivationProcessLoopback,
            ProcessLoopbackParams = new ProcessLoopbackParams { TargetProcessId = pid, ProcessLoopbackMode = LoopbackIncludeTargetTree },
        };
        _body = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParams>());
        _props = Marshal.AllocHGlobal(Marshal.SizeOf<PlPropVariant>());
        _iid = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.StructureToPtr(wanted, _body, false);
            // The blob lives in the PlPropVariant by value: its size at +8, its pointer at +16.
            Marshal.StructureToPtr(new PlPropVariant
            {
                Type = VtBlob,
                Size = (uint)Marshal.SizeOf<ActivationParams>(),
                Data = _body,
            }, _props, false);
            Marshal.Copy(GuidAudioClient.ToByteArray(), 0, _iid, 16);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The process to listen to, as the mixer currently sees it. Zero when it has none.</summary>
    public static uint ResolvePid(string appId)
    {
        if (appId.Length == 0) return 0;
        try
        {
            foreach (uint pid in AudioService.SessionProcesses())
            {
                if (SourceApp.OwnsProcess(appId, pid)) return pid;
            }
        }
        catch (Exception ex) { App.Log(ex); }
        return 0;
    }

    /// <summary>Activates the client, waits for the engine to hand it over, and starts it.</summary>
        public bool Open()
    {
        int hr = PlApi.ActivateAudioInterfaceAsync(Device, _iid, _props, _self, out IntPtr operation);
        if (hr != Ok) return false;
        if (operation != IntPtr.Zero) Marshal.Release(operation);

        var callback = _callback!;
        if (!callback.Done.Wait(TimeSpan.FromSeconds(5))) return false;
        if (callback.Hr != Ok || callback.ActivateResult != Ok || callback.Client == IntPtr.Zero) return false;

        _client = callback.Client;
        callback.Client = IntPtr.Zero; // ownership passes to this object

        IntPtr format = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
        try
        {
            Marshal.StructureToPtr(new WaveFormatEx
            {
                Format = FormatPcm,
                Channels = Channels,
                SamplesPerSec = Rate,
                BitsPerSample = Bits,
                BlockAlign = Channels * Bits / 8,
                AvgBytesPerSec = Rate * Channels * Bits / 8,
            }, format, false);

            int flags = StreamLoopback | AutoConvertPcm | SourceDefaultQuality;
            if (Call<InitializeFn>(_client, ClientInitialize)(_client, ShareShared, flags, BufferDuration, 0, format, IntPtr.Zero) != Ok)
                return false;

            IntPtr iid = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.Copy(GuidCaptureClient.ToByteArray(), 0, iid, 16);
                if (Call<GetServiceFn>(_client, ClientGetService)(_client, iid, out _capture) != Ok || _capture == IntPtr.Zero)
                    return false;
            }
            finally
            {
                Marshal.FreeHGlobal(iid);
            }

            return Call<UInt32Fn>(_client, ClientStart)(_client) == Ok;
        }
        finally
        {
            Marshal.FreeHGlobal(format);
        }
    }

    /// <summary>Next packet of the target process's audio, or zero frames when there is nothing yet.</summary>
    public bool Next(out IntPtr data, out int frames, out bool silent)
    {
        data = IntPtr.Zero;
        frames = 0;
        silent = false;
        if (_capture == IntPtr.Zero) return false;

        var nextPacket = Call<GetUintOutFn>(_capture, CaptureGetNextPacketSize);
        if (nextPacket(_capture, out uint pending) != Ok || pending == 0) return true;

        if (Call<GetBufferFn>(_capture, CaptureGetBuffer)(_capture, out data, out uint available, out int flags, out _, out _) != Ok)
            return false;
        if (available == 0)
        {
            Call<ReleaseBufferFn>(_capture, CaptureReleaseBuffer)(_capture, 0);
            return true;
        }

        frames = (int)available;
        silent = (flags & 2) != 0; // AUDCLNT_BUFFERFLAGS_SILENT
        return true;
    }

    public void Release(int frames) =>
        Call<ReleaseBufferFn>(_capture, CaptureReleaseBuffer)(_capture, (uint)frames);

    public void Dispose()
    {
        if (_client != IntPtr.Zero)
        {
            try { Call<UInt32Fn>(_client, ClientStop)(_client); } catch { }
        }
        if (_capture != IntPtr.Zero) { try { Marshal.Release(_capture); } catch { } _capture = IntPtr.Zero; }
        if (_client != IntPtr.Zero) { try { Marshal.Release(_client); } catch { } _client = IntPtr.Zero; }
        Free(ref _iid);
        Free(ref _props);
        Free(ref _body);
        _callback?.Dispose();
        _callback = null;
    }

    static void Free(ref IntPtr p)
    {
        if (p == IntPtr.Zero) return;
        Marshal.FreeHGlobal(p);
        p = IntPtr.Zero;
    }

    /// <summary>Address of the nth vtable entry, counting IUnknown's three first.</summary>
    static IntPtr Slot(IntPtr comObject, int slot) =>
        Marshal.ReadIntPtr(Marshal.ReadIntPtr(comObject), slot * IntPtr.Size);

    static T Call<T>(IntPtr comObject, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Slot(comObject, slot));

    /// <summary>The COM object the engine calls back on; see the remarks on the class.</summary>
    sealed class Callback : IDisposable
    {
        // One activation is in flight at a time, so the calls find their state through this field.
        static Callback? Current;

        readonly IntPtr _self, _vtable;
        int _refCount = 1;

        // Held in fields so the delegates, and the pointers taken from them, outlive the call.
        readonly QueryInterfaceFn _queryInterface = QueryInterface;
        readonly AddRefFn _addRef = AddRef;
        readonly ReleaseFn _release = Release;
        readonly CompletedFn _completed = ActivateCompleted;

        public readonly ManualResetEventSlim Done = new(false);
        public int Hr = -1, ActivateResult = -1;
        public IntPtr Client;

        public Callback()
        {
            _vtable = Marshal.AllocHGlobal(IntPtr.Size * 4);
            Marshal.WriteIntPtr(_vtable, 0 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_queryInterface));
            Marshal.WriteIntPtr(_vtable, 1 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_addRef));
            Marshal.WriteIntPtr(_vtable, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_release));
            Marshal.WriteIntPtr(_vtable, 3 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_completed));

            _self = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(_self, _vtable);
            Current = this;
        }

        public IntPtr Self => _self;

        static int QueryInterface(IntPtr self, IntPtr iid, IntPtr result)
        {
            var callback = Current;
            if (callback == null || result == IntPtr.Zero || iid == IntPtr.Zero) return NoInterface;

            Guid asked = ReadGuid(iid);
            if (asked == GuidAgile || asked == GuidHandler || asked == GuidUnknown)
            {
                // All three name this one object, so the reference count still governs its lifetime.
                Interlocked.Increment(ref callback._refCount);
                Marshal.WriteIntPtr(result, self);
                return Ok;
            }
            Marshal.WriteIntPtr(result, IntPtr.Zero);
            return NoInterface;
        }

        static Guid ReadGuid(IntPtr iid)
        {
            Span<byte> bytes = stackalloc byte[16];
                    for (int i = 0; i < 16; i++) bytes[i] = Marshal.ReadByte(iid, i);
            return new Guid(bytes);
        }

        static uint AddRef(IntPtr self)
        {
            var callback = Current;
            return callback == null ? 0 : (uint)Interlocked.Increment(ref callback._refCount);
        }

        static uint Release(IntPtr self)
        {
            var callback = Current;
            return callback == null ? 0 : (uint)Interlocked.Decrement(ref callback._refCount);
        }

        static int ActivateCompleted(IntPtr self, IntPtr operation)
        {
            var callback = Current;
            if (callback == null) return 0;
            try
            {
                // The operation is used straight away: the engine is free to release it afterwards.
                var getResult = Call<GetActivateResultFn>(operation, OpGetActivateResult);
                callback.Hr = getResult(operation, out callback.ActivateResult, out callback.Client);
            }
            catch (Exception ex) { App.Log(ex); }
            finally { callback.Done.Set(); }
            return 0;
        }

        public void Dispose()
        {
            Done.Dispose();
            Marshal.FreeHGlobal(_self);
            Marshal.FreeHGlobal(_vtable);
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
struct ProcessLoopbackParams { public uint TargetProcessId; public int ProcessLoopbackMode; }

[StructLayout(LayoutKind.Sequential)]
struct ActivationParams { public int ActivationType; public ProcessLoopbackParams ProcessLoopbackParams; }

// PlPropVariant's union is 8-byte aligned, so the blob size lands at +8 and its pointer at +16.
[StructLayout(LayoutKind.Sequential)]
struct PlPropVariant
{
    public ushort Type;
    ushort _r1, _r2, _r3;
    public uint Size;
    uint _pad;
    public IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
struct WaveFormatEx
{
    public ushort Format, Channels;
    public int SamplesPerSec, AvgBytesPerSec;
    public ushort BlockAlign, BitsPerSample, ExtraSize;
}

static class PlApi
{
    [DllImport("mmdevapi.dll", EntryPoint = "ActivateAudioInterfaceAsync", ExactSpelling = true)]
    public static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string device,
        IntPtr iid,
        IntPtr activationParams,
        IntPtr completionHandler,
        out IntPtr operation);
}

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int QueryInterfaceFn(IntPtr self, IntPtr iid, IntPtr result);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate uint AddRefFn(IntPtr self);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate uint ReleaseFn(IntPtr self);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int CompletedFn(IntPtr self, IntPtr operation);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int GetActivateResultFn(IntPtr self, out int activateResult, out IntPtr audioInterface);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int InitializeFn(IntPtr self, int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int GetServiceFn(IntPtr self, IntPtr iid, out IntPtr service);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int GetBufferFn(IntPtr self, out IntPtr data, out uint frames, out int flags, out long devicePosition, out long qpcPosition);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int GetUintOutFn(IntPtr self, out uint value);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate uint UInt32Fn(IntPtr self);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int ReleaseBufferFn(IntPtr self, uint frames);


