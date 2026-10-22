using System.Buffers;

namespace BEngine.Audio;

internal sealed class AudioMixerGraph : IDisposable
{
    private readonly Dictionary<BusKey, Bus> _buses = [];
    private readonly List<BusKey> _order = [];
    private readonly List<BusKey> _roots = [], _removed = [];
    private readonly HashSet<BusKey> _rootSet = [], _visited = [];
    private readonly List<AudioMixerGroup> _groups = [];
    private readonly AudioMixerRouting.ValidationBatch _validation = new();
    private readonly FrameDataBuffer<BusWork> _work = new();
    private int _sampleRate, _channels, _length;
    private int _clockRate;
    private double _frameRemainder;
    internal string? Error { get; set; }
    internal int BusCount => _buses.Count;
    internal bool HasTail { get { foreach (var bus in _buses.Values) if (bus.HasTail) return true; return false; } }
    internal int AllocateFrames(Fix64 deltaTime, int rate)
    {
        if (_clockRate != rate) { _clockRate = rate; _frameRemainder = 0; }
        // A background window can legitimately update only once per second.
        // Do not truncate those frames to 250ms and starve the device queue.
        var exact = Math.Clamp((double)deltaTime, 0, 2) * rate + _frameRemainder;
        var count = (int)Math.Round(exact, MidpointRounding.AwayFromZero);
        _frameRemainder = exact - count; return count;
    }

    internal void Prepare(IReadOnlyList<AudioSource> sources, int sampleRate, int channels, int length)
    {
        if (_sampleRate != sampleRate || _channels != channels) Dispose();
        _sampleRate = sampleRate; _channels = channels; _length = length;
        _roots.Clear(); _rootSet.Clear(); _groups.Clear(); _visited.Clear(); _order.Clear(); _work.Clear();
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            if (source.enabled && source.gameObject.activeInHierarchy && source.outputAudioMixerGroup is not null) AddRoot(KeyFor(source));
        }
        foreach (var pair in _buses) if (pair.Value.HasTail && IsAlive(pair.Key)) AddRoot(pair.Key);
        _validation.Validate(_groups);
        foreach (var key in _roots) Visit(key, _visited);
        if ((long)_order.Count * length > 16 * 1024 * 1024) throw new InvalidDataException("Mixer bus sample buffers exceed 64 MiB.");
        long delaySamples = 0;
        foreach (var key in _order)
            foreach (var effect in key.Group.effects)
                if (effect.enabled && effect.type == AudioMixerEffectType.Delay)
                    delaySamples += Math.Max(1, (int)Math.Round((double)effect.delaySeconds * sampleRate)) * channels;
        if (delaySamples > 8 * 1024 * 1024) throw new InvalidDataException("Mixer delay buffers exceed 32 MiB per scene.");
        _removed.Clear();
        foreach (var key in _buses.Keys) if (!_visited.Contains(key)) _removed.Add(key);
        foreach (var key in _removed) { _buses[key].Dispose(); _buses.Remove(key); }
        _removed.Clear(); _roots.Clear(); _rootSet.Clear(); _groups.Clear(); _visited.Clear();
        _order.Reverse();
        foreach (var key in _order)
        {
            if (!_buses.TryGetValue(key, out var bus)) _buses.Add(key, bus = new());
            bus.Buffer = ArrayPool<float>.Shared.Rent(length); bus.Buffer.AsSpan(0, length).Clear();
            _work.Add(new(key, bus));
        }
        void AddRoot(BusKey key) { if (_rootSet.Add(key)) { _roots.Add(key); _groups.Add(key.Group); } }
    }
    private void Visit(BusKey key, HashSet<BusKey> visited)
    {
        if (!visited.Add(key)) return;
        if (visited.Count > 256) throw new InvalidDataException("Audio mixer supports at most 256 scene/controller buses.");
        if (key.Group.parent is { } parent) Visit(new(parent, key.Controller), visited);
        foreach (var send in key.Group.sends) Visit(new(send.target!, key.Controller), visited);
        _order.Add(key);
    }
    internal Span<float> BufferFor(AudioSource source) => _buses[KeyFor(source)].Buffer.AsSpan(0, _length);

    internal void Process(Span<float> output)
    {
        foreach (ref readonly var entry in _work.WrittenSpan)
        {
            var key = entry.Key; var bus = entry.Bus; var samples = bus.Buffer.AsSpan(0, _length);
            bus.Process(samples, _sampleRate, _channels, key.Group.effects);
            foreach (var send in key.Group.sends)
                if (send.preFader) Add(samples, _buses[new(send.target!, key.Controller)].Buffer.AsSpan(0, _length), (float)send.volume);
            var gain = (float)(key.Controller is { } controller ? controller.GetVolume(key.Group) :
                key.Group.mute ? Fix64.Zero : Mathf.Clamp01(key.Group.volume));
            using (LoopExecutionGuard.AllowIterations(_length + 1))
                for (var i = 0; i < _length; i++) samples[i] *= gain;
            foreach (var send in key.Group.sends)
                if (!send.preFader) Add(samples, _buses[new(send.target!, key.Controller)].Buffer.AsSpan(0, _length), (float)send.volume);
            Add(samples, key.Group.parent is { } parent ? _buses[new(parent, key.Controller)].Buffer.AsSpan(0, _length) : output, 1);
        }
    }
    private static void Add(ReadOnlySpan<float> source, Span<float> target, float gain)
    {
        using var budget = LoopExecutionGuard.AllowIterations(source.Length + 1);
        for (var i = 0; i < source.Length; i++) target[i] += source[i] * gain;
    }
    internal void FinishFrame()
    {
        foreach (var bus in _buses.Values)
            if (bus.Buffer.Length > 0) { ArrayPool<float>.Shared.Return(bus.Buffer); bus.Buffer = []; }
    }
    public void Dispose()
    {
        FinishFrame(); foreach (var bus in _buses.Values) bus.Dispose(); _buses.Clear(); _order.Clear();
        _work.Clear(); _roots.Clear(); _rootSet.Clear(); _groups.Clear(); _visited.Clear(); _removed.Clear();
    }
    private static bool IsAlive(BusKey key) => !key.Group.isDestroyed && (key.Controller is null ||
        !key.Controller.isDestroyed && key.Controller.enabled && key.Controller.gameObject.activeInHierarchy);
    private static BusKey KeyFor(AudioSource source) => new(source.outputAudioMixerGroup!,
        source.mixerController is { isDestroyed: false, enabled: true } controller && controller.gameObject.activeInHierarchy &&
        ReferenceEquals(controller.gameObject.scene, source.gameObject.scene) ? controller : null);
    private readonly record struct BusKey(AudioMixerGroup Group, AudioMixerController? Controller);
    private readonly record struct BusWork(BusKey Key, Bus Bus);
    private sealed class Bus : IDisposable
    {
        internal float[] Buffer = [];
        private readonly List<(AudioMixerEffect Settings, AudioDspProcessor? Processor)> _effects = [];
        internal bool HasTail { get { foreach (var entry in _effects) if (entry.Processor?.HasTail == true) return true; return false; } }
        internal void Process(Span<float> samples, int sampleRate, int channels, List<AudioMixerEffect> settings)
        {
            if (_effects.Count > settings.Count)
            {
                foreach (var entry in _effects.Skip(settings.Count)) entry.Processor?.Dispose();
                _effects.RemoveRange(settings.Count, _effects.Count - settings.Count);
            }
            for (var i = 0; i < settings.Count; i++)
            {
                var effect = settings[i];
                if (_effects.Count == i) _effects.Add((effect, null));
                var entry = _effects[i];
                if (!ReferenceEquals(effect, entry.Settings) || entry.Processor?.Type != effect.type || !effect.enabled)
                { entry.Processor?.Dispose(); entry = (effect, null); }
                if (effect.enabled)
                {
                    entry.Processor ??= new AudioDspProcessor(effect.type, channels);
                    _effects[i] = entry;
                    entry.Processor.Process(samples, sampleRate, effect);
                }
                _effects[i] = entry;
            }
        }
        public void Dispose() { foreach (var entry in _effects) entry.Processor?.Dispose(); _effects.Clear(); }
    }
}
