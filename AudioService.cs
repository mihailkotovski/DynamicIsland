using System.Runtime.InteropServices;

namespace DynamicIsland;

sealed class AudioService
{
    const uint HeadphonesFormFactor = 3, HeadsetFormFactor = 5;
    const int SessionExpired = 2;
    static readonly TimeSpan DeviceCheckInterval = TimeSpan.FromSeconds(1);

    static readonly Guid DeviceFormat = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    static readonly PropertyKey DeviceDesc = new(DeviceFormat, 2);
    static readonly PropertyKey FriendlyName = new(DeviceFormat, 14);
    static readonly PropertyKey InterfaceName = new(new("026e516e-b814-414b-83cd-856d6fef4822"), 2);
    static readonly PropertyKey FormFactor = new(new("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);
    static readonly PropertyKey ContainerId = new(new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);

    public readonly record struct Output(string Kind, string Name, bool Headphones, Guid Container);

    IAudioEndpointVolume? _volume;
    IAudioMeterInformation? _meter;
    DateTime _checkedAt = DateTime.MinValue;
    string? _deviceId;
    bool _initialized;
    Output? _switchedTo;

    public Output? Device { get; private set; }

    public bool TryTakeSwitch(out Output device)
    {
        device = _switchedTo ?? default;
        bool switched = _switchedTo != null;
        _switchedTo = null;
        return switched;
    }

    public bool TryGetVolume(out float level, out bool muted)
    {
        level = 0;
        muted = false;
        RefreshDevice();
        if (_volume == null) return false;
        if (_volume.GetMasterVolumeLevelScalar(out level) == 0 && _volume.GetMute(out muted) == 0) return true;

        ReleaseDevice();
        return false;
    }

    public float Peak() => _meter != null && _meter.GetPeakValue(out float peak) == 0 ? peak : -1;

    public void AdjustVolume(float delta)
    {
        RefreshDevice();
        if (_volume == null || _volume.GetMasterVolumeLevelScalar(out float level) != 0) return;
        Guid context = Guid.Empty;
        _volume.SetMasterVolumeLevelScalar(Math.Clamp(level + delta, 0f, 1f), ref context);
    }

    public bool AdjustAppVolume(string appId, float delta, out float level)
    {
        level = 0;
        bool found = false;
        try
        {
            foreach (object session in Sessions())
            {
                if (session is not IAudioSessionControl2 control || session is not ISimpleAudioVolume volume) continue;
                if (control.GetState(out int state) != 0 || state == SessionExpired) continue;
                if (control.GetProcessId(out uint process) < 0 || !SourceApp.OwnsProcess(appId, process)) continue;

                if (!found)
                {
                    if (volume.GetMasterVolume(out level) != 0) continue;
                    level = Math.Clamp(level + delta, 0f, 1f);
                    found = true;
                }
                Guid context = Guid.Empty;
                volume.SetMasterVolume(level, ref context);
                if (delta > 0) volume.SetMute(false, ref context);
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        return found;
    }

    /// <summary>The processes that have a sound session on the default playback device right now.</summary>
    public static IEnumerable<uint> SessionProcesses()
    {
        if (Mixer() is not { } mixer) yield break;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            if (mixer.GetSessionEnumerator(out sessions) != 0 || sessions == null || sessions.GetCount(out int count) != 0) yield break;
            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out object? session) != 0 || session == null) continue;
                try
                {
                    if (session is IAudioSessionControl2 control && control.GetProcessId(out uint process) == 0 && process != 0)
                        yield return process;
                }
                finally
                {
                    Marshal.ReleaseComObject(session);
                }
            }
        }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            Marshal.ReleaseComObject(mixer);
        }
    }

    static IEnumerable<object> Sessions()
    {
        IAudioSessionManager2? mixer = Mixer();
        if (mixer == null) yield break;

        IAudioSessionEnumerator? sessions = null;
        try
        {
            if (mixer.GetSessionEnumerator(out sessions) != 0 || sessions == null || sessions.GetCount(out int count) != 0) yield break;
            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out object? session) != 0 || session == null) continue;
                try
                {
                    yield return session;
                }
                finally
                {
                    Marshal.ReleaseComObject(session);
                }
            }
        }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            Marshal.ReleaseComObject(mixer);
        }
    }

    static IAudioSessionManager2? Mixer()
    {
        IMMDevice? device = AudioEndpoint.Default();
        if (device == null) return null;
        try
        {
            return device.Activate<IAudioSessionManager2>();
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    void RefreshDevice()
    {
        if (DateTime.UtcNow - _checkedAt < DeviceCheckInterval) return;
        _checkedAt = DateTime.UtcNow;
        try
        {
            IMMDevice? device = AudioEndpoint.Default();
            try
            {
                string? id = null;
                if (device == null || device.GetId(out id) != 0 || id == null)
                {
                    id = null;
                    ReleaseDevice();
                    Device = null;
                }
                else if (id != _deviceId || _volume == null)
                {
                    ReleaseDevice();
                    _volume = device.Activate<IAudioEndpointVolume>();
                    _meter = device.Activate<IAudioMeterInformation>();
                    Device = Describe(device);
                    if (_initialized && id != _deviceId) _switchedTo = Device;
                }
                _deviceId = id;
                _initialized = true;
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    static Output Describe(IMMDevice device)
    {
        if (device.OpenPropertyStore(0, out IPropertyStore? store) != 0 || store == null) return new Output("", "", false, Guid.Empty);
        try
        {
            string kind = store.GetString(DeviceDesc);
            return new Output(
                kind.Length > 0 ? kind : store.GetString(FriendlyName),
                store.GetString(InterfaceName),
                store.GetUInt32(FormFactor) is HeadphonesFormFactor or HeadsetFormFactor,
                store.GetGuid(ContainerId));
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    void ReleaseDevice()
    {
        if (_volume != null) Marshal.ReleaseComObject(_volume);
        if (_meter != null) Marshal.ReleaseComObject(_meter);
        _volume = null;
        _meter = null;
    }
}
