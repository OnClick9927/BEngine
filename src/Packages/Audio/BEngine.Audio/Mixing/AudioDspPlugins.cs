using System.Runtime.InteropServices;

namespace BEngine.Audio;

public interface IAudioDspPlugin : IDisposable
{
    bool HasTail { get; }
    void Process(Span<float> interleavedSamples, ReadOnlySpan<float> parameters);
}

/// <summary>Factories create independent state for each scene/controller/bus effect instance.</summary>
public static class AudioDspPlugins
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Registration> Factories = new(StringComparer.Ordinal);

    public static IDisposable Register(string id, Func<int, int, IAudioDspPlugin> factory) => Register(id, factory, null);

    public static IDisposable RegisterNative(string id, string libraryPath)
    {
        if (OperatingSystem.IsBrowser() || OperatingSystem.IsIOS())
            throw new PlatformNotSupportedException("Dynamic native DSP libraries are unavailable on this target. Register a compiled managed/static adapter instead.");
        var library = new NativeFactory(Path.GetFullPath(libraryPath));
        try { return Register(id, library.Create, library.Release); }
        catch { library.Release(); throw; }
    }

    private static IDisposable Register(string id, Func<int, int, IAudioDspPlugin> factory, Action? release)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentNullException.ThrowIfNull(factory);
        lock (Gate)
        {
            if (Factories.ContainsKey(id)) throw new InvalidOperationException("DSP plugin is already registered: " + id);
            var registration = new Registration(id, factory, release); Factories.Add(id, registration); return registration;
        }
    }

    public static IAudioDspPlugin Create(string id, int sampleRate, int channels)
    {
        if (sampleRate is < 8000 or > 192000 || channels is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        lock (Gate)
            return Factories.TryGetValue(id, out var registration)
                ? registration.Factory(sampleRate, channels) ?? throw new InvalidOperationException("DSP factory returned no instance: " + id)
                : throw new InvalidOperationException("DSP plugin is not registered: " + id);
    }

    private sealed class Registration(string id, Func<int, int, IAudioDspPlugin> factory, Action? release) : IDisposable
    {
        internal Func<int, int, IAudioDspPlugin> Factory { get; } = factory;
        public void Dispose()
        {
            lock (Gate)
            {
                if (!Factories.TryGetValue(id, out var current) || !ReferenceEquals(current, this)) return;
                Factories.Remove(id); release?.Invoke();
            }
        }
    }

    private sealed unsafe class NativeFactory
    {
        private readonly object _gate = new();
        private nint _library;
        private int _references = 1;
        private readonly delegate* unmanaged[Cdecl]<int, int, nint> _create;
        internal readonly delegate* unmanaged[Cdecl]<nint, void> Destroy;
        internal readonly delegate* unmanaged[Cdecl]<nint, float*, int, void> Process;
        internal readonly delegate* unmanaged[Cdecl]<nint, int, float, void> SetParameter;
        internal readonly delegate* unmanaged[Cdecl]<nint, int> HasTail;

        internal NativeFactory(string path)
        {
            _library = NativeLibrary.Load(path);
            try
            {
                var version = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(_library, "bengine_dsp_abi_version");
                if (version() != 1) throw new NotSupportedException("Native DSP plugin ABI must be version 1.");
                _create = (delegate* unmanaged[Cdecl]<int, int, nint>)NativeLibrary.GetExport(_library, "bengine_dsp_create");
                Destroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(_library, "bengine_dsp_destroy");
                Process = (delegate* unmanaged[Cdecl]<nint, float*, int, void>)NativeLibrary.GetExport(_library, "bengine_dsp_process");
                SetParameter = (delegate* unmanaged[Cdecl]<nint, int, float, void>)NativeLibrary.GetExport(_library, "bengine_dsp_set_parameter");
                HasTail = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(_library, "bengine_dsp_has_tail");
            }
            catch { NativeLibrary.Free(_library); _library = 0; throw; }
        }

        internal IAudioDspPlugin Create(int sampleRate, int channels)
        {
            lock (_gate)
            {
                if (_library == 0) throw new ObjectDisposedException(nameof(NativeFactory));
                var state = _create(sampleRate, channels);
                if (state == 0) throw new InvalidOperationException("Native DSP plugin rejected its format or allocation failed.");
                _references++; return new NativeInstance(this, state, channels);
            }
        }

        internal void Release()
        {
            lock (_gate)
                if (--_references == 0) { NativeLibrary.Free(_library); _library = 0; }
        }
    }

    private sealed unsafe class NativeInstance(NativeFactory owner, nint state, int channels) : IAudioDspPlugin
    {
        private nint _state = state;
        private int _parameterCount;
        public bool HasTail => _state != 0 && owner.HasTail(_state) != 0;
        public void Process(Span<float> samples, ReadOnlySpan<float> parameters)
        {
            ObjectDisposedException.ThrowIf(_state == 0, this);
            if (samples.Length % channels != 0) throw new ArgumentException("DSP samples must contain complete interleaved frames.", nameof(samples));
            for (var index = 0; index < Math.Max(parameters.Length, _parameterCount); index++)
                owner.SetParameter(_state, index, index < parameters.Length ? parameters[index] : 0);
            _parameterCount = parameters.Length;
            fixed (float* buffer = samples) owner.Process(_state, buffer, samples.Length / channels);
        }
        public void Dispose()
        {
            var previous = Interlocked.Exchange(ref _state, 0);
            if (previous == 0) return;
            try { owner.Destroy(previous); } finally { owner.Release(); }
        }
    }
}
